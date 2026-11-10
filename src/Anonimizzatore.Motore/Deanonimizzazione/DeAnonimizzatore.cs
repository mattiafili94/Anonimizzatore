using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Deanonimizzazione;

/// <summary>Un token che ha la forma di un nome anonimo ma non è presente nella mappa del cliente.</summary>
/// <param name="Token">Il testo trovato, così come scritto dall'IA.</param>
/// <param name="Posizione">Indice del token nel testo ricevuto (prima della de-anonimizzazione).</param>
public sealed record TokenNonTrovato(string Token, int Posizione);

/// <summary>Risultato di <see cref="DeAnonimizzatore.DeAnonimizza"/>.</summary>
/// <param name="Testo">Il testo con i nomi originali rimessi al loro posto.</param>
/// <param name="Sostituzioni">Quanti nomi anonimi sono stati sostituiti.</param>
/// <param name="NonTrovati">Token che sembrano anonimi ma senza corrispondenza: restano com'erano nel testo.</param>
/// <param name="Commenti">Esito del ripristino dei commenti (segnaposto <c>/* c001 */</c>): quali l'IA ha perso, quali sono sconosciuti.</param>
public sealed record RisultatoDeAnonimizzazione(
    string Testo, int Sostituzioni, IReadOnlyList<TokenNonTrovato> NonTrovati, Commenti.RisultatoRipristino? Commenti = null)
{
    /// <summary>
    /// Il report per l'utente, già in frasi (con la riga nel testo ricevuto, 0 se non c'è): nomi anonimi senza corrispondenza
    /// (l'IA può averli inventati), commenti che l'IA ha tolto o spostato, segnaposto <c>&lt;password&gt;</c> e SID da riempire a mano.
    /// Vuoto se è tornato tutto.
    /// </summary>
    public IReadOnlyList<Avviso> Avvisi { get; init; } = [];
}

/// <summary>
/// Rimette i nomi originali in una risposta dell'IA (testo libero: prosa + codice).
/// <list type="bullet">
/// <item>Riconosce i nomi nel formato del motore (<c>tbl_001</c>, <c>srv_01</c>, <c>col_vc50_012</c>,
/// <c>@p_vc20_002</c>, <c>#tmp_003</c>), senza distinguere maiuscole e minuscole.</item>
/// <item>Li trova anche dentro nomi composti inventati dall'IA (<c>FK_tbl_001_tbl_002</c>), ma non dentro parole
/// più lunghe (<c>tbl_0012</c>, <c>xtbl_001</c>, <c>tbl_001a</c>).</item>
/// <item>Colonne e variabili si risolvono dal solo numero (vedi <see cref="MappaCliente.TrovaOriginale"/>).</item>
/// <item>Le parentesi quadre scritte dall'IA restano; se il nome originale va quotato (spazi, simboli, parola
/// riservata) e l'IA non le aveva messe, vengono aggiunte.</item>
/// </list>
/// I segnaposto dei commenti (<c>/* c001 */</c>) non sono di competenza di questa classe.
/// </summary>
public sealed partial class DeAnonimizzatore
{
    private const string Tipo = @"(?:(?:dec|num)[0-9]{1,3}_[0-9]{1,3}|[a-z][a-z0-9]*)";
    private const string Numero = "[0-9]{1,9}";

    // Un token con la forma di un nome anonimo. A sinistra niente lettere/cifre/@/# (ma il "_" va bene: nomi composti),
    // a destra niente lettere/cifre (un "_" va bene).
    private const string FormaToken =
        @"(?<![\p{L}\p{N}@#])(?:" +
        $"(?:srv|db|sch|dom|ag|tbl|vw|usp|fn|trg|job|usr|rol|ix|pk|fk|df|ck|uq|al)_{Numero}" +
        $"|col_{Tipo}_{Numero}" +
        $"|@[vp]_{Tipo}_{Numero}" +
        $"|##?tmp_{Numero}" +
        @")(?![\p{L}\p{N}])";

    [GeneratedRegex(FormaToken, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    // Un segmento tra parentesi quadre (con "]]" come "]" letterale) oppure una "parola" di identificatore.
    [GeneratedRegex(@"\[(?<dentro>(?:[^\]\r\n]|\]\])*)\]|(?<parola>[@#]*[\p{L}\p{N}_$]+)")]
    private static partial Regex Segmento();

    [GeneratedRegex(@"[@#]*[\p{L}\p{N}_$]+")]
    private static partial Regex Parola();

    [GeneratedRegex(@"^[\p{L}_@#][\p{L}\p{N}_@#$]*$")]
    private static partial Regex IdentificatoreRegolare();

    [GeneratedRegex(@"^[\p{L}\p{N}_@#$]+$")]
    private static partial Regex SoloCaratteriDiIdentificatore();

    private static readonly HashSet<string> ParoleRiservate = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "any", "as", "asc", "authorization", "backup", "begin", "between", "break",
        "browse", "bulk", "by", "cascade", "case", "check", "checkpoint", "close", "clustered", "coalesce", "collate",
        "column", "commit", "compute", "constraint", "contains", "continue", "convert", "create", "cross", "current",
        "cursor", "database", "dbcc", "deallocate", "declare", "default", "delete", "deny", "desc", "disk", "distinct",
        "distributed", "double", "drop", "dump", "else", "end", "errlvl", "escape", "except", "exec", "execute",
        "exists", "exit", "external", "fetch", "file", "fillfactor", "for", "foreign", "freetext", "from", "full",
        "function", "goto", "grant", "group", "having", "holdlock", "identity", "if", "in", "index", "inner",
        "insert", "intersect", "into", "is", "join", "key", "kill", "left", "like", "lineno", "load", "merge",
        "national", "nocheck", "nonclustered", "not", "null", "nullif", "of", "off", "offsets", "on", "open",
        "option", "or", "order", "outer", "over", "percent", "pivot", "plan", "precision", "primary", "print",
        "proc", "procedure", "public", "raiserror", "read", "readtext", "reconfigure", "references", "replication",
        "restore", "restrict", "return", "revert", "revoke", "right", "rollback", "rowcount", "rule", "save",
        "schema", "select", "session_user", "set", "setuser", "shutdown", "some", "statistics", "system_user",
        "table", "tablesample", "textsize", "then", "to", "top", "tran", "transaction", "trigger", "truncate",
        "try_convert", "tsequal", "union", "unique", "unpivot", "update", "updatetext", "use", "user", "values",
        "varying", "view", "waitfor", "when", "where", "while", "with", "writetext",
    };

    private readonly MappaCliente _mappa;
    private readonly Infrastruttura.AnalizzatoreInfrastruttura _infrastruttura;

    public DeAnonimizzatore(MappaCliente mappa)
    {
        ArgumentNullException.ThrowIfNull(mappa);
        _mappa = mappa;
        // Testo libero: in una risposta dell'IA uno spazio chiude un percorso.
        _infrastruttura = new Infrastruttura.AnalizzatoreInfrastruttura(mappa, new Infrastruttura.OpzioniInfrastruttura(TestoLibero: true));
    }

    /// <summary>De-anonimizza il testo della risposta dell'IA.</summary>
    public RisultatoDeAnonimizzazione DeAnonimizza(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        // 1. Infrastruttura: percorsi, URL, IP, email, stringhe di connessione. Per prima, perché lì i nomi di server e
        //    database stanno dentro un percorso (\\srv_01\dir_001\db_01.bak) e non vanno messi tra parentesi quadre.
        var infrastruttura = _infrastruttura.RipristinaNeiContesti(testo);
        testo = infrastruttura.Testo;

        // 2. Nomi degli oggetti (con le parentesi quadre dove servono).
        var passata = new Passata();
        var sb = new StringBuilder(testo.Length);
        var ultimo = 0;

        foreach (Match m in Segmento().Matches(testo))
        {
            sb.Append(testo, ultimo, m.Index - ultimo);
            ultimo = m.Index + m.Length;

            if (m.Groups["dentro"].Success)
            {
                // Già tra parentesi quadre: le parentesi restano, dentro si mettono i nomi originali "crudi".
                var dentro = m.Groups["dentro"];
                sb.Append('[').Append(SostituisciInParentesi(dentro.Value, dentro.Index, passata)).Append(']');
            }
            else
            {
                sb.Append(SostituisciParola(testo, m, passata));
            }
        }

        sb.Append(testo, ultimo, testo.Length - ultimo);

        // I commenti per ultimi: il loro testo originale non deve passare dalla ricerca dei nomi (un commento può
        // contenere qualcosa che assomiglia a un nome anonimo).
        var commenti = _mappa.Commenti.Ripristina(sb.ToString());
        return new RisultatoDeAnonimizzazione(
            commenti.Testo, passata.Sostituzioni + infrastruttura.Sostituzioni.Count, passata.NonTrovati, commenti)
        {
            Avvisi = CostruisciReport(testo, commenti.Testo, passata, infrastruttura, commenti, _mappa.Commenti.Commenti.Count),
        };
    }

    // --- report ---

    private const int MassimoNelMessaggio = 8;

    [GeneratedRegex(@"<password>")]
    private static partial Regex SegnapostoPassword();

    [GeneratedRegex(@"0x0{32}\b")]
    private static partial Regex SegnapostoSid();

    /// <param name="testoIntermedio">Il testo dopo il ripristino dell'infrastruttura: le posizioni di <c>NonTrovati</c> sono sue
    /// (le righe sono quelle della risposta dell'IA, perché i ripristini non aggiungono né tolgono a capo).</param>
    private static List<Avviso> CostruisciReport(
        string testoIntermedio, string testoFinale, Passata passata, Infrastruttura.RisultatoRipristino infra,
        Commenti.RisultatoRipristino commenti, int commentiNellaMappa)
    {
        var avvisi = new List<Avviso>();

        // Nomi con la forma di un nome anonimo ma senza corrispondenza: un oggetto nuovo proposto dall'IA, oppure un nome sbagliato.
        var visti = new HashSet<string>(NomeSql.Confronto);
        foreach (var gruppo in passata.NonTrovati.GroupBy(t => t.Token, NomeSql.Confronto))
        {
            visti.Add(gruppo.Key);
            var volte = gruppo.Count() > 1 ? $" ({gruppo.Count()} volte)" : "";
            avvisi.Add(new Avviso(
                $"'{gruppo.Key}' ha la forma di un nome anonimo ma non è nella mappa{volte}: l'ho lasciato com'è. " +
                "Può essere un oggetto nuovo proposto dall'IA oppure un nome che ha cambiato.",
                RigaDi(testoIntermedio, gruppo.First().Posizione)));
        }
        foreach (var nome in infra.NonTrovati.Where(visti.Add))
            avvisi.Add(new Avviso($"'{nome}' ha la forma di un nome anonimo ma non è nella mappa: l'ho lasciato com'è.", 0));

        // Commenti. Se nella risposta non c'è nessun segnaposto l'IA ha scritto solo un pezzo di codice (il caso più comune):
        // non ha "perso" nulla, semplicemente non ha riscritto quel codice. Si segnala solo se ne ha conservati alcuni e altri no.
        var segnapostoNelTesto = commentiNellaMappa - commenti.Persi.Count + commenti.Sconosciuti.Count > 0;
        if (commenti.Persi.Count > 0 && segnapostoNelTesto)
            avvisi.Add(new Avviso(
                $"L'IA ha tolto {commenti.Persi.Count} {(commenti.Persi.Count == 1 ? "commento" : "commenti")} dello script originale " +
                $"({Elenco(commenti.Persi.Select(GestoreCommentiSegnaposto))}): non sono tornati.", 0));
        if (commenti.Sconosciuti.Count > 0)
            avvisi.Add(new Avviso(
                $"Segnaposto di commento che non esistono nella mappa ({Elenco(commenti.Sconosciuti.Select(GestoreCommentiSegnaposto))}): lasciati come sono.", 0));
        if (commenti.Spostati.Count > 0)
            avvisi.Add(new Avviso(
                $"Dei commenti '--' sono stati messi su una riga a sé, per non commentare il codice che l'IA ha scritto dopo " +
                $"({Elenco(commenti.Spostati.Select(GestoreCommentiSegnaposto))}).", 0));

        // Segreti: non si ripristinano mai.
        var password = SegnapostoPassword().Matches(testoFinale);
        if (password.Count > 0)
            avvisi.Add(new Avviso(
                $"Nel testo {(password.Count == 1 ? "c'è 1 segnaposto" : $"ci sono {password.Count} segnaposto")} <password>: le password " +
                "non si ripristinano, vanno inserite a mano.", RigaDi(testoFinale, password[0].Index)));
        var sid = SegnapostoSid().Matches(testoFinale);
        if (sid.Count > 0)
            avvisi.Add(new Avviso(
                $"Nel testo {(sid.Count == 1 ? "c'è 1 SID" : $"ci sono {sid.Count} SID")} azzerato: il SID vero non si ripristina, va inserito a mano.",
                RigaDi(testoFinale, sid[0].Index)));

        return avvisi;
    }

    private static string GestoreCommentiSegnaposto(int numero) => Commenti.GestoreCommenti.Segnaposto(numero);

    private static string Elenco(IEnumerable<string> voci)
    {
        var tutte = voci.ToList();
        return string.Join(", ", tutte.Take(MassimoNelMessaggio)) + (tutte.Count > MassimoNelMessaggio ? $" e altri {tutte.Count - MassimoNelMessaggio}" : "");
    }

    /// <summary>Numero di riga (da 1) di una posizione del testo.</summary>
    private static int RigaDi(string testo, int posizione)
    {
        var righe = 1;
        for (var i = 0; i < posizione && i < testo.Length; i++)
            if (testo[i] == '\n') righe++;
        return righe;
    }

    // Un nome anonimo scritto con un numero diverso dal nostro (tbl_1 invece di tbl_001): succede, si prova con la larghezza giusta.
    [GeneratedRegex(@"^(?<f>[A-Za-z]+)_(?<n>[0-9]{1,9})$")]
    private static partial Regex NomeConNumero();

    private VoceMappa? Cerca(string token)
    {
        if (_mappa.TrovaOriginale(token) is { } trovata) return trovata;

        var m = NomeConNumero().Match(token);
        if (!m.Success) return null;
        var numero = int.Parse(m.Groups["n"].Value);
        foreach (var larghezza in new[] { 2, 3 })
        {
            var candidato = $"{m.Groups["f"].Value}_{numero.ToString("D" + larghezza)}";
            if (!candidato.Equals(token, StringComparison.Ordinal) && _mappa.TrovaOriginale(candidato) is { } voce) return voce;
        }
        return null;
    }

    private string SostituisciInParentesi(string dentro, int offset, Passata passata)
    {
        var sb = new StringBuilder(dentro.Length);
        var ultimo = 0;
        foreach (Match p in Parola().Matches(dentro))
        {
            sb.Append(dentro, ultimo, p.Index - ultimo);
            ultimo = p.Index + p.Length;
            var r = SostituisciToken(p.Value, offset + p.Index, passata, inParentesi: true);
            sb.Append(r.Testo);
        }

        sb.Append(dentro, ultimo, dentro.Length - ultimo);
        return sb.ToString();
    }

    private string SostituisciParola(string testo, Match m, Passata passata)
    {
        var parola = m.Value;
        var r = SostituisciToken(parola, m.Index, passata, inParentesi: false);
        if (r.Sostituite.Count == 0) return parola;

        // Se l'IA l'aveva messo tra doppi apici, sono già un quoting valido.
        var traApici = m.Index > 0 && testo[m.Index - 1] == '"'
            && m.Index + m.Length < testo.Length && testo[m.Index + m.Length] == '"';
        if (traApici) return r.Testo;

        // Il nome è tutta una stringa ('job_001', N'srv_01'): è un valore, non un identificatore, quindi niente
        // parentesi quadre (che finirebbero dentro la stringa); gli apici dentro il nome si raddoppiano.
        var traApiciSingoli = m.Index > 0 && testo[m.Index - 1] == '\''
            && m.Index + m.Length < testo.Length && testo[m.Index + m.Length] == '\'';
        if (traApiciSingoli) return r.Testo.Replace("'", "''");

        var daQuotare = r.CopreTutto
            ? DaQuotare(r.Sostituite[0])
            : r.Sostituite.Any(v => !SoloCaratteri(v));
        return daQuotare ? Quota(r.Testo) : r.Testo;
    }

    private (string Testo, List<VoceMappa> Sostituite, bool CopreTutto) SostituisciToken(
        string parola, int offset, Passata passata, bool inParentesi)
    {
        var sostituite = new List<VoceMappa>();
        var matches = Token().Matches(parola);
        if (matches.Count == 0) return (parola, sostituite, false);

        var sb = new StringBuilder(parola.Length);
        var ultimo = 0;
        foreach (Match t in matches)
        {
            sb.Append(parola, ultimo, t.Index - ultimo);
            ultimo = t.Index + t.Length;

            var voce = Cerca(t.Value);
            if (voce is null)
            {
                passata.NonTrovati.Add(new TokenNonTrovato(t.Value, offset + t.Index));
                sb.Append(t.Value);
                continue;
            }

            passata.Sostituzioni++;
            sostituite.Add(voce);
            sb.Append(inParentesi ? voce.Originale.Replace("]", "]]") : voce.Originale);
        }

        sb.Append(parola, ultimo, parola.Length - ultimo);
        var copreTutto = matches.Count == 1 && matches[0].Length == parola.Length && sostituite.Count == 1;
        return (sb.ToString(), sostituite, copreTutto);
    }

    private static string Quota(string nome) => "[" + nome.Replace("]", "]]") + "]";

    private sealed class Passata
    {
        public int Sostituzioni;
        public List<TokenNonTrovato> NonTrovati { get; } = [];
    }

    // Logica del quoting.
    private static bool DaQuotare(VoceMappa voce)
    {
        // Variabili, parametri e temporanee portano già @ o #: tra parentesi non sarebbero valide.
        if (voce.Ruolo is Ruolo.Variabile or Ruolo.Parametro or Ruolo.Temporanea) return false;
        return !IdentificatoreRegolare().IsMatch(voce.Originale) || ParoleRiservate.Contains(voce.Originale);
    }

    private static bool SoloCaratteri(VoceMappa voce) =>
        voce.Ruolo is Ruolo.Variabile or Ruolo.Parametro or Ruolo.Temporanea
        || SoloCaratteriDiIdentificatore().IsMatch(voce.Originale);
}
