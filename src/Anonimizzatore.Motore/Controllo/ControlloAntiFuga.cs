using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Controllo;

/// <summary>
/// L'ultima rete di sicurezza, dopo l'anonimizzazione e prima di copiare. Non si fida di niente: rilegge il <b>risultato</b>.
/// <list type="bullet">
/// <item>cerca i <b>nomi originali</b> che la mappa ha anonimizzato: in una stringa o in una riga SQLCMD è quasi certamente una
/// fuga (<see cref="GravitaFuga.Bloccante"/>: testi liberi, <c>NAME = 'Vendite full'</c> di un backup, il percorso di un URL);
/// come identificatore è di solito una colonna o un alias lasciati in chiaro per scelta, quindi solo un avviso (a meno che lo
/// script sia stato letto in modalità di riserva);</item>
/// <item>cerca le <b>parole della blacklist</b>, ovunque: sempre bloccante;</item>
/// <item>cerca <b>password</b> non cancellate (<c>PASSWORD = '...'</c>, <c>Pwd=...</c>, <c>-P ...</c>) senza mai ripeterne il valore;</item>
/// <item>cerca <b>commenti</b> che non siano segnaposto;</item>
/// <item>rilegge il risultato: se lo script originale era T-SQL valido e quello anonimizzato non lo è più, è un errore del motore;</item>
/// <item>avvisa di <b>dati personali</b> rimasti nelle stringhe (email, codice fiscale, IBAN, carta, IP, URL): i dati non sono nella v1.</item>
/// </list>
/// </summary>
public static partial class ControlloAntiFuga
{
    /// <param name="scriptOriginali">Gli script come sono stati incollati (servono a sapere se erano T-SQL valido).</param>
    /// <param name="risultato">Il risultato dell'anonimizzazione, nello stesso ordine.</param>
    /// <param name="schemaAnonimizzato">Il testo dello schema allegato, se c'è (si controlla anche quello).</param>
    public static EsitoControllo Verifica(
        IReadOnlyList<string> scriptOriginali, RisultatoAnonimizzazione risultato, MappaCliente mappa, Profilo profilo,
        string? schemaAnonimizzato = null)
    {
        ArgumentNullException.ThrowIfNull(scriptOriginali);
        ArgumentNullException.ThrowIfNull(risultato);
        ArgumentNullException.ThrowIfNull(mappa);
        ArgumentNullException.ThrowIfNull(profilo);
        if (scriptOriginali.Count != risultato.Script.Count)
            throw new ArgumentException("Gli script originali e i risultati devono essere lo stesso numero.", nameof(scriptOriginali));

        var ricerca = new Ricerca(mappa, profilo);
        var fughe = new List<Fuga>();

        for (var i = 0; i < risultato.Script.Count; i++)
        {
            var testo = risultato.Script[i].Testo;
            if (testo is null) continue; // non verrà copiato

            var validoPrima = PrimoErrore(scriptOriginali[i]) is null;
            ControllaTesto(testo, i, validoPrima, ricerca, fughe);

            if (validoPrima && PrimoErrore(testo) is { } errore)
                fughe.Add(new Fuga(TipoFuga.ScriptNonValido, GravitaFuga.Bloccante, "", i, errore.Riga,
                    $"Lo script originale era T-SQL valido, quello anonimizzato non più ({errore.Messaggio}): è un errore dell'app, non copiarlo."));
        }

        if (!string.IsNullOrWhiteSpace(schemaAnonimizzato)) ControllaTesto(schemaAnonimizzato, -1, true, ricerca, fughe);

        var ordinate = fughe
            .DistinctBy(f => (f.Tipo, f.Valore.ToLowerInvariant(), f.Script, f.Riga))
            .OrderBy(f => f.Script < 0 ? int.MaxValue : f.Script).ThenBy(f => f.Riga).ThenBy(f => f.Tipo)
            .ToList();
        return new EsitoControllo(ordinate);
    }

    // --- lettura del testo, pezzo per pezzo ---

    private static void ControllaTesto(string testo, int script, bool validoPrima, Ricerca r, List<Fuga> fughe)
    {
        foreach (var seg in Sqlcmd.Dividi(testo))
        {
            Segreti(seg.Testo, script, seg.RigaIniziale, fughe);

            // Una riga SQLCMD è tutta "testo": nomi, parole della blacklist, dati.
            if (seg.Sqlcmd)
            {
                CercaInTesto(seg.Testo, script, seg.RigaIniziale, "una riga SQLCMD", r, fughe, datiPersonali: true);
                continue;
            }

            var letto = LettoreScript.Leggi(seg.Testo);
            if (letto.Token.Count == 0)
            {
                // Il lessico non ha dato token: si guarda il testo così com'è.
                CercaInTesto(seg.Testo, script, seg.RigaIniziale, "il testo", r, fughe, datiPersonali: true);
                continue;
            }

            foreach (var token in letto.Token)
            {
                var riga = seg.RigaIniziale + token.Line - 1;
                switch (token.TokenType)
                {
                    case TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral:
                        var contenuto = SqlDinamico.Spacchetta(token.Text)?.Contenuto ?? token.Text;
                        CercaInTesto(contenuto, script, riga, "una stringa", r, fughe, datiPersonali: true);
                        break;

                    case TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment:
                        if (!SegnapostoCommento().IsMatch(token.Text.Trim()))
                        {
                            fughe.Add(new Fuga(TipoFuga.CommentoNonSostituito, GravitaFuga.Bloccante, "", script, riga,
                                "C'è un commento che non è un segnaposto: il suo testo non è stato sostituito."));
                            CercaInTesto(token.Text, script, riga, "un commento", r, fughe, datiPersonali: false);
                        }
                        break;

                    case TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier or TSqlTokenType.Variable
                        or TSqlTokenType.SqlCommandIdentifier:
                        ControllaIdentificatore(token.Text, script, riga, validoPrima, r, fughe);
                        break;
                }
            }
        }
    }

    private static void ControllaIdentificatore(string testo, int script, int riga, bool validoPrima, Ricerca r, List<Fuga> fughe)
    {
        var nome = NomeSql.Normalizza(testo);
        if (nome.Length == 0 || nome.Contains("$(", StringComparison.Ordinal) || EAnonimo(nome)) return;

        if (r.Blacklist.Contiene(nome))
        {
            fughe.Add(new Fuga(TipoFuga.ParolaBlacklist, GravitaFuga.Bloccante, nome, script, riga,
                $"'{nome}' contiene una parola della blacklist."));
        }
        else if (r.Originali.Contains(nome))
        {
            // Una colonna, un alias o una variabile con lo stesso nome di un oggetto anonimizzato (restano in chiaro per scelta);
            // in modalità di riserva non si sa cosa sia: meglio fermarsi.
            fughe.Add(new Fuga(TipoFuga.NomeOriginale, validoPrima ? GravitaFuga.Avviso : GravitaFuga.Bloccante, nome, script, riga,
                $"'{nome}' è il nome vero di un oggetto anonimizzato e compare come identificatore" +
                (validoPrima ? ": va bene se è una colonna o un alias lasciati in chiaro." : " in uno script letto in modalità di riserva: controlla.")));
        }
    }

    /// <summary>Cerca in un testo libero (stringa, commento, riga SQLCMD) i nomi originali, la blacklist e, se richiesto, i dati personali.</summary>
    private static void CercaInTesto(string testo, int script, int rigaInizio, string dove, Ricerca r, List<Fuga> fughe, bool datiPersonali)
    {
        foreach (Match m in r.OriginaliRegex?.Matches(testo) ?? (MatchCollection)EmptyMatches)
        {
            var nome = (m.Groups["b"].Success ? m.Groups["b"] : m.Groups["n"]).Value;
            fughe.Add(new Fuga(TipoFuga.NomeOriginale, GravitaFuga.Bloccante, nome, script, Riga(rigaInizio, testo, m.Index),
                $"Il nome '{nome}', anonimizzato altrove, compare ancora in {dove}."));
        }

        foreach (Match m in ParolaIdentificatore().Matches(testo))
        {
            if (EAnonimo(m.Value) || !r.Blacklist.Contiene(m.Value)) continue;
            fughe.Add(new Fuga(TipoFuga.ParolaBlacklist, GravitaFuga.Bloccante, m.Value, script, Riga(rigaInizio, testo, m.Index),
                $"'{m.Value}' contiene una parola della blacklist ({dove})."));
        }
        foreach (Match m in r.FrasiBlacklist?.Matches(testo) ?? (MatchCollection)EmptyMatches)
            fughe.Add(new Fuga(TipoFuga.ParolaBlacklist, GravitaFuga.Bloccante, m.Value, script, Riga(rigaInizio, testo, m.Index),
                $"'{m.Value}' è una voce della blacklist ({dove})."));

        if (datiPersonali) DatiPersonali(testo, script, rigaInizio, dove, fughe);
    }

    // Un MatchCollection vuoto da usare dove una regex può non esserci.
    private static readonly MatchCollection EmptyMatches = Regex.Matches("", "x");

    private static int Riga(int rigaInizio, string testo, int indice) => rigaInizio + testo.Take(indice).Count(c => c == '\n');

    // --- segreti ---

    private static void Segreti(string testo, int script, int rigaInizio, List<Fuga> fughe)
    {
        foreach (Match m in ChiaveSegreta().Matches(testo))
        {
            var valore = m.Groups["v"].Value.Trim();
            if (SegnapostoNoto(valore) || valore.StartsWith('@') || valore.Length == 0) continue;
            fughe.Add(new Fuga(TipoFuga.Segreto, GravitaFuga.Bloccante, m.Groups["k"].Value.ToUpperInvariant(), script,
                Riga(rigaInizio, testo, m.Index), $"Sembra rimasta una password dopo {m.Groups["k"].Value.ToUpperInvariant()} (il valore non si mostra)."));
        }
        foreach (Match m in OpzionePassword().Matches(testo))
        {
            var valore = m.Groups["v"].Value.Trim('"');
            if (SegnapostoNoto(valore)) continue;
            fughe.Add(new Fuga(TipoFuga.Segreto, GravitaFuga.Bloccante, "-P", script, Riga(rigaInizio, testo, m.Index),
                "Sembra rimasta una password dopo l'opzione -P di una riga di comando (il valore non si mostra)."));
        }
    }

    private static bool SegnapostoNoto(string valore) => valore is "<password>" or "0x00" || valore.StartsWith("<password>", StringComparison.Ordinal);

    // --- dati personali (solo avvisi) ---

    private static void DatiPersonali(string testo, int script, int rigaInizio, string dove, List<Fuga> fughe)
    {
        void Avvisa(string valore, int indice, string cosa) =>
            fughe.Add(new Fuga(TipoFuga.DatoPersonale, GravitaFuga.Avviso, cosa, script, Riga(rigaInizio, testo, indice),
                $"In {dove} sembra esserci {cosa}. I dati (livello 4) non sono nella v1: se è un dato vero, toglilo a mano."));

        foreach (Match m in Email().Matches(testo))
            if (!m.Value.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase)) Avvisa(m.Value, m.Index, "un indirizzo email");
        foreach (Match m in CodiceFiscale().Matches(testo)) Avvisa(m.Value, m.Index, "un codice fiscale");
        foreach (Match m in Iban().Matches(testo))
            if (IbanValido(m.Value)) Avvisa(m.Value, m.Index, "un IBAN");
        foreach (Match m in NumeroCarta().Matches(testo))
            if (LuhnValido(m.Value)) Avvisa(m.Value, m.Index, "un numero di carta di credito");
        foreach (Match m in Ipv4().Matches(testo))
            if (m.Value.Split('.').All(p => int.Parse(p) <= 255) && m.Value is not ("0.0.0.0" or "127.0.0.1")) Avvisa(m.Value, m.Index, "un indirizzo IP");
        foreach (Match m in Url().Matches(testo))
            if (!Regex.IsMatch(m.Value, @"://(?:srv|dom)_\d+", RegexOptions.IgnoreCase)) Avvisa(m.Value, m.Index, "un URL");
    }

    private static bool IbanValido(string iban)
    {
        var s = iban.Replace(" ", "").ToUpperInvariant();
        if (s.Length is < 15 or > 34) return false;
        var spostato = s[4..] + s[..4];
        var resto = 0;
        foreach (var c in spostato)
        {
            var valore = char.IsDigit(c) ? c - '0' : c - 'A' + 10;
            resto = (valore < 10 ? resto * 10 + valore : resto * 100 + valore) % 97;
        }
        return resto == 1;
    }

    private static bool LuhnValido(string numero)
    {
        var cifre = numero.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (cifre.Length is < 13 or > 19) return false;
        var somma = 0;
        for (var i = 0; i < cifre.Length; i++)
        {
            var c = cifre[^(i + 1)];
            if (i % 2 == 1) { c *= 2; if (c > 9) c -= 9; }
            somma += c;
        }
        return somma % 10 == 0;
    }

    // --- validità del T-SQL ---

    /// <summary>Il primo errore di sintassi del testo (le righe SQLCMD e i blocchi vuoti non contano), o <c>null</c> se si legge.</summary>
    private static (int Riga, string Messaggio)? PrimoErrore(string testo)
    {
        foreach (var seg in Sqlcmd.Dividi(testo).Where(s => !s.Sqlcmd && !string.IsNullOrWhiteSpace(s.Testo)))
        {
            var letto = LettoreScript.Leggi(seg.Testo);
            if (!letto.Valido) return (seg.RigaIniziale + letto.Errori[0].Line - 1, letto.Errori[0].Message);
        }
        return null;
    }

    // --- che cosa è un nome anonimo ---

    private static bool EAnonimo(string nome)
    {
        // DOMINIO\utente anonimizzato: dom_01\usr_001
        if (nome.Contains('\\')) return nome.Split('\\').All(p => NomeAnonimo().IsMatch(p));
        return NomeAnonimo().IsMatch(nome);
    }

    // --- ciò che si cerca: costruito una volta dalla mappa e dal profilo ---

    private sealed class Ricerca
    {
        public Ricerca(MappaCliente mappa, Profilo profilo)
        {
            Blacklist = new Blacklist(profilo.Blacklist);

            foreach (var voce in mappa.Voci)
                if (voce.Originale.Length >= 2 && !voce.Originale.Contains('\\') && !voce.Originale.Contains(']'))
                    Originali.Add(voce.Originale);
            if (Originali.Count > 0)
            {
                var alternative = string.Join("|", Originali.OrderByDescending(o => o.Length).Select(Regex.Escape));
                OriginaliRegex = new Regex(@"(?<![\w@#$])(?:\[(?<b>" + alternative + @")\]|(?<n>" + alternative + @"))(?![\w$])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            // Le voci della blacklist fatte di più parole ("Acme Srl") si cercano anche separate da spazi, punti, trattini.
            var frasi = profilo.Blacklist.Select(v => Parole.Dividi(v)).Where(p => p.Count >= 2)
                .Select(p => string.Join(@"[\s_.\-]*", p.Select(Regex.Escape))).ToList();
            if (frasi.Count > 0)
                FrasiBlacklist = new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", frasi) + @")(?![\p{L}\p{N}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public Blacklist Blacklist { get; }
        public HashSet<string> Originali { get; } = new(NomeSql.Confronto);
        public Regex? OriginaliRegex { get; }
        public Regex? FrasiBlacklist { get; }
    }

    // --- espressioni regolari ---

    [GeneratedRegex(@"^/\*\s*c\d{3,}\s*\*/$", RegexOptions.IgnoreCase)]
    private static partial Regex SegnapostoCommento();

    [GeneratedRegex(@"[\p{L}\p{N}_@#$]+")]
    private static partial Regex ParolaIdentificatore();

    [GeneratedRegex(
        @"^(?:(?:srv|db|sch|dom|ag|txt|tbl|vw|usp|fn|trg|usr|rol|job|ix|pk|fk|df|ck|uq|al|dir|file|ip)_\d+" +
        @"|col_[a-z][a-z0-9]*(?:_\d+)?_\d+|@[vp]_[a-z][a-z0-9]*(?:_\d+)?_\d+|##?tmp_\d+|mail_\d+@example\.invalid)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NomeAnonimo();

    // PASSWORD = 'x' | PASSWORD = 0x... | Password=x; | Pwd=x | SECRET = 'x'
    [GeneratedRegex(
        @"(?<![\w@])(?<k>old_password|password|passwd|pwd|secret)\b\s*=\s*(?:N?'(?<v>(?:[^']|'')*)'|(?<v>0x[0-9a-fA-F]+)|(?<v>[^\s;,)'""\[\]][^\s;,)'""]*))",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChiaveSegreta();

    [GeneratedRegex(@"(?<![\w-])-P[ \t]+(?<v>""[^""]*""|[^\s""]+)")]
    private static partial Regex OpzionePassword();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{6}\d{2}[A-EHLMPR-T]\d{2}[A-Z]\d{3}[A-Z](?![A-Za-z0-9])")]
    private static partial Regex CodiceFiscale();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){2,7}(?: ?[A-Z0-9]{1,4})?(?![A-Za-z0-9])")]
    private static partial Regex Iban();

    [GeneratedRegex(@"(?<![\d-])\d(?:[ -]?\d){12,18}(?![\d-])")]
    private static partial Regex NumeroCarta();

    [GeneratedRegex(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w]|\.\d)")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"\bhttps?://[^\s'""<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}
