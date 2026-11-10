using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Infrastruttura;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Anonimizzazione;

/// <summary>Esito per un singolo script.</summary>
/// <param name="Testo">Lo script anonimizzato; <c>null</c> se non è stato possibile (es. errori di sintassi).</param>
public sealed record ScriptAnonimizzato(string? Testo, IReadOnlyList<Avviso> Avvisi)
{
    public bool Anonimizzato => Testo is not null;
}

public sealed record RisultatoAnonimizzazione(IReadOnlyList<ScriptAnonimizzato> Script);

/// <summary>
/// Punto d'ingresso del motore. Tutti gli script di una chiamata condividono la stessa mappa. Passi:
/// <list type="number">
/// <item>lettura (ScriptDom). Ogni stringa che contiene T-SQL valido (SQL dinamico) o la dichiarazione dei parametri di
/// <c>sp_executesql</c> diventa una <i>sotto-unità</i> trattata come uno script a sé, in modo ricorsivo;</item>
/// <item>primo giro su tutte le unità (CREATE, tipi, constraint, alias) e classificazione dei nomi;</item>
/// <item>decisione per ogni nome: sistema → in chiaro; blacklist → anonimizzato; altrimenti la matrice del profilo;</item>
/// <item>fase A: nomi di oggetti, colonne, variabili e alias; fase B: nomi composti di indici e constraint
/// (<c>FK_Righe_Ordini</c> → <c>FK_tbl_001_tbl_002</c>), quando tutti gli oggetti hanno già il loro nome anonimo;</item>
/// <item>sostituzione sui token del testo originale; password e SID cancellati;</item>
/// <item>fase C, dalle unità più interne alle più esterne: stringhe (percorsi, IP, email... e pezzi di SQL costruito a
/// mano) e commenti; il SQL dinamico anonimizzato torna dentro la stringa che lo conteneva.</item>
/// </list>
/// </summary>
/// <remarks>
/// Nome diverso da "Anonimizzatore" apposta: sarebbe uguale al primo pezzo del namespace e creerebbe ambiguità.
/// </remarks>
public static class AnonimizzatoreScript
{
    // Ruoli i cui nomi sono "composti" (IX_Ordini_Data): se non vanno anonimizzati per intero si sostituiscono le parti.
    private static readonly HashSet<Ruolo> RuoliComposti =
        [Ruolo.Indice, Ruolo.PrimaryKey, Ruolo.ForeignKey, Ruolo.Default, Ruolo.Check, Ruolo.Unique];

    // Ruoli di cui le parti dei nomi composti vengono sostituite.
    private static readonly HashSet<Ruolo> RuoliDelleParti =
    [
        Ruolo.Tabella, Ruolo.Vista, Ruolo.Procedura, Ruolo.Funzione, Ruolo.Trigger,
        Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer, Ruolo.Server,
    ];

    // Come SQL Server riconosce un SID o una password "svuotati" (restano T-SQL validi).
    private const string SidSegnaposto = "0x00000000000000000000000000000000";
    private const string PasswordSegnaposto = "<password>";

    /// <summary>
    /// Uno script, oppure una stringa di SQL dinamico trovata dentro un altro (la sua <see cref="Padre"/>). Le unità si
    /// elaborano insieme, con la stessa mappa; alla fine ogni figlia torna dentro la stringa della madre.
    /// </summary>
    private sealed class Unita(ScriptLetto letto)
    {
        public ScriptLetto Letto { get; } = letto;
        public Unita? Padre { get; init; }
        public int TokenNelPadre { get; init; } = -1;
        public string Prefisso { get; init; } = "";
        public string Suffisso { get; init; } = "";
        public int Profondita { get; init; }

        public PreAnalisi? Pre { get; set; }
        public RisultatoClassificazione? Classificazione { get; set; }

        /// <summary>Vero se la stringa è stata riconosciuta come un nome o un segreto (<c>EXECUTE AS USER = '...'</c>): non è SQL.</summary>
        public bool Scartata { get; set; }

        public IReadOnlyList<NomeTrovato> Nomi => Classificazione?.Nomi ?? [];
        public Dictionary<int, string> Sostituzioni { get; } = [];

        /// <summary>Le stringhe di SQL dinamico di questa unità, già anonimizzate (senza prefisso e suffisso), per posizione.</summary>
        public Dictionary<int, string> Figlie { get; } = [];
        public List<Avviso> AvvisiDelleFiglie { get; } = [];

        /// <summary>Posizioni delle stringhe che sono nomi o segreti (non possono essere SQL).</summary>
        public HashSet<int> StringheReclamate() =>
            Nomi.Where(n => n.DaStringa).Select(n => n.IndiceToken)
                .Concat(Classificazione?.Segreti.Select(s => s.IndiceToken) ?? []).ToHashSet();
    }

    public static RisultatoAnonimizzazione Anonimizza(IReadOnlyList<string> script, Profilo profilo, MappaCliente mappa)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(profilo);
        ArgumentNullException.ThrowIfNull(mappa);

        var unita = script.Select(s => new Unita(LettoreScript.Leggi(s))).ToList();
        var radici = unita.Count;

        // SQL dinamico: ogni stringa che è T-SQL valido è un'unità figlia; vale anche per quelle dentro le figlie.
        // (la lista cresce mentre la si scorre: le figlie vengono dopo le madri)
        for (var i = 0; i < unita.Count; i++)
        {
            var u = unita[i];
            if (!u.Letto.Valido || u.Profondita >= SqlDinamico.ProfonditaMassima) continue;
            foreach (var f in SqlDinamico.TrovaFigli(u.Letto))
                unita.Add(new Unita(f.Letto)
                {
                    Padre = u, TokenNelPadre = f.Token, Prefisso = f.Prefisso, Suffisso = f.Suffisso, Profondita = u.Profondita + 1,
                });
        }

        // I nomi veri presenti negli script non devono mai coincidere con un nome generato.
        foreach (var u in unita)
            mappa.AggiungiNomiRiservati(u.Letto.Token
                .Where(t => t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                .Select(t => t.Text));

        // Giro 1 su tutte le unità: i CREATE e i tipi di una servono a classificare i riferimenti delle altre.
        var conoscenza = Conoscenza.Da(mappa);
        foreach (var u in unita.Where(u => u.Letto.Valido))
        {
            u.Pre = new PreAnalisi(conoscenza);
            u.Letto.Albero.Accept(u.Pre);
        }

        // Giro 2: classificazione.
        foreach (var u in unita.Where(u => u.Pre is not null))
            u.Classificazione = Classificatore.Classifica(u.Letto.Albero, conoscenza, u.Pre!);

        // Una stringa riconosciuta come nome o segreto (EXECUTE AS USER = '...') non è SQL, anche se per caso si legge.
        foreach (var u in unita.Where(u => u.Padre is not null))
            u.Scartata = u.Padre!.Scartata || u.Padre.StringheReclamate().Contains(u.TokenNelPadre);
        var attive = unita.Where(u => !u.Scartata).ToList();

        var liste = new Liste(profilo, mappa);
        var infrastruttura = new Infra(new AnalizzatoreInfrastruttura(mappa),
            new AnalizzatoreInfrastruttura(mappa, new OpzioniInfrastruttura(TestoLibero: true)));

        // Fase A: oggetti, colonne, variabili e alias, nell'ordine in cui compaiono.
        foreach (var u in attive)
            foreach (var nome in u.Nomi.Where(n => !RuoliComposti.Contains(n.Ruolo) && liste.DaAnonimizzare(n)))
                u.Sostituzioni[nome.IndiceToken] =
                    Riquota(NomeAnonimo(nome, mappa, conoscenza, liste, infrastruttura), nome, u.Letto.Token[nome.IndiceToken].Text);

        // Fase B: indici e constraint.
        var parti = PartiSostituibili(mappa);
        foreach (var u in attive)
            foreach (var nome in u.Nomi.Where(n => RuoliComposti.Contains(n.Ruolo) && !n.DiSistema))
            {
                // Per intero se la blacklist o la matrice lo chiedono, o se le parti non si possono sostituire in modo
                // pulito; altrimenti solo le parti che sono oggetti anonimizzati.
                var nuovo = liste.DaAnonimizzare(nome) ? null : SostituisciParti(nome.Nome, parti);
                nuovo ??= mappa.Ottieni(nome.Ruolo, nome.Nome);
                if (nuovo != nome.Nome)
                    u.Sostituzioni[nome.IndiceToken] = Riquota(nuovo, nome, u.Letto.Token[nome.IndiceToken].Text);
            }

        // Fase C, dalle unità più interne alle più esterne: le figlie sono dopo le madri nella lista.
        var dizionario = new SqlDinamico.Dizionario(mappa);
        // Prima le stringhe di SQL dinamico, dalle più interne; poi gli script, nell'ordine in cui sono stati incollati
        // (così anche i segnaposto dei commenti seguono quell'ordine).
        for (var i = unita.Count - 1; i >= radici; i--)
        {
            var u = unita[i];
            if (u.Scartata) continue;

            var (testo, avvisi) = Ricomponi(u, mappa, infrastruttura, dizionario);

            // Il SQL dinamico anonimizzato torna dentro la stringa della madre (senza il testo aggiunto per leggerlo).
            if (testo is not null && testo.StartsWith(u.Prefisso, StringComparison.Ordinal) && testo.EndsWith(u.Suffisso, StringComparison.Ordinal))
                u.Padre!.Figlie[u.TokenNelPadre] = testo[u.Prefisso.Length..^u.Suffisso.Length];

            var riga = u.Padre!.Letto.Token[u.TokenNelPadre].Line;
            u.Padre.AvvisiDelleFiglie.AddRange(avvisi.Select(a => new Avviso($"SQL dinamico: {a.Messaggio}", riga)));
        }

        var risultati = new List<ScriptAnonimizzato>();
        for (var i = 0; i < radici; i++)
        {
            var (testo, avvisi) = Ricomponi(unita[i], mappa, infrastruttura, dizionario);
            risultati.Add(new ScriptAnonimizzato(testo, avvisi));
        }

        return new RisultatoAnonimizzazione(risultati);
    }

    private static (string? Testo, List<Avviso> Avvisi) Ricomponi(
        Unita u, MappaCliente mappa, Infra infrastruttura, SqlDinamico.Dizionario dizionario)
    {
        if (u.Pre is null)
        {
            // La modalità di riserva (solo token) arriva nella tappa 10: per ora meglio non restituire nulla
            // che potrebbe contenere nomi in chiaro.
            var primo = u.Letto.Errori[0];
            return (null, [new Avviso($"Script con errori di sintassi, non anonimizzato: {primo.Message}", primo.Line)]);
        }

        var testi = u.Letto.Token.Select(t => t.Text).ToArray();
        foreach (var (indice, testo) in u.Sostituzioni) testi[indice] = testo;

        // Password e SID: sempre cancellati, qualunque sia il profilo. Non passano dalla mappa e non vengono salvati.
        foreach (var segreto in u.Classificazione!.Segreti)
            testi[segreto.IndiceToken] = TestoSegnaposto(segreto, u.Letto.Token[segreto.IndiceToken].Text);

        var avvisi = new List<Avviso>(u.Classificazione.Avvisi);
        AnonimizzaStringhe(u, testi, infrastruttura, dizionario, avvisi);
        avvisi.AddRange(u.AvvisiDelleFiglie);

        // Ultimo passo: i commenti diventano segnaposto (livello 5). Il testo originale resta nella mappa del cliente.
        return (mappa.Commenti.Anonimizza(string.Concat(testi)), avvisi);
    }

    /// <summary>
    /// Le stringhe che non sono nomi né segreti:
    /// <list type="bullet">
    /// <item>SQL dinamico valido: al posto della stringa va quella già anonimizzata dall'unità figlia;</item>
    /// <item>pezzi di SQL che non si possono leggere da soli (<c>' FROM '</c>, SQL costruito con <c>+</c>, SQL con errori):
    /// si sostituiscono per dizionario i nomi già noti e si avvisa;</item>
    /// <item>tutte: percorsi (<c>BACKUP ... TO DISK = '...'</c>), IP, host, email, URL, stringhe di connessione.</item>
    /// </list>
    /// </summary>
    private static void AnonimizzaStringhe(
        Unita u, string[] testi, Infra infrastruttura, SqlDinamico.Dizionario dizionario, List<Avviso> avvisi)
    {
        var daSaltare = u.Sostituzioni.Keys.Concat(u.Classificazione!.Segreti.Select(s => s.IndiceToken)).ToHashSet();
        var comandi = u.Classificazione.Comandi.ToHashSet();

        void Avvisa(string messaggio, int riga)
        {
            var avviso = new Avviso(messaggio, riga);
            if (!avvisi.Contains(avviso)) avvisi.Add(avviso);
        }

        for (var i = 0; i < u.Letto.Token.Count; i++)
        {
            var token = u.Letto.Token[i];
            if (token.TokenType is not (TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral)
                || daSaltare.Contains(i))
                continue;
            if (SqlDinamico.Spacchetta(testi[i]) is not { } stringa) continue;

            if (u.Figlie.TryGetValue(i, out var figlia))
            {
                testi[i] = SqlDinamico.Ricompatta(stringa.Prefisso, figlia);
                continue;
            }

            var contenuto = stringa.Contenuto;
            if (SqlDinamico.InizioFrammento(contenuto))
            {
                contenuto = dizionario.Sostituisci(contenuto);
                Avvisa("SQL dinamico costruito a pezzi o con errori di sintassi: ho sostituito per dizionario i nomi già noti; " +
                       "controlla che non ne restino altri (per esempio quelli aggiunti con una variabile).", token.Line);
            }
            else if (SqlDinamico.EUnNomeOggetto(contenuto))
            {
                // OBJECT_ID('dbo.Ordini'), @tabella = N'Ordini': una stringa che è solo il nome di un oggetto già noto.
                contenuto = dizionario.Sostituisci(contenuto);
            }

            // Una riga di comando (xp_cmdshell, passo di job CmdExec) è testo libero: uno spazio chiude il percorso.
            var analizzatore = comandi.Contains(i) ? infrastruttura.TestoLibero : infrastruttura.Letterali;
            var esito = analizzatore.Anonimizza(contenuto);
            foreach (var a in esito.Avvisi) Avvisa(a, token.Line);
            if (!esito.Testo.Equals(stringa.Contenuto, StringComparison.Ordinal))
                testi[i] = SqlDinamico.Ricompatta(stringa.Prefisso, esito.Testo);
        }
    }

    /// <summary>I due modi di leggere una stringa: come contenuto di un letterale (un percorso può avere spazi) o come testo libero.</summary>
    private sealed record Infra(AnalizzatoreInfrastruttura Letterali, AnalizzatoreInfrastruttura TestoLibero);

    /// <summary>
    /// La decisione per ogni nome, in quest'ordine: oggetto di sistema → in chiaro; blacklist → anonimizzato (vince
    /// su tutto); tipo di oggetto in chiaro nella matrice → in chiaro; whitelist → in chiaro, salvo che il nome abbia
    /// già un nome anonimo (i nomi assegnati restano stabili: si cambiano solo con "rigenera nomi"); altrimenti
    /// anonimizzato.
    /// </summary>
    private sealed class Liste(Profilo profilo, MappaCliente mappa)
    {
        private readonly Blacklist _blacklist = new(profilo.Blacklist);
        private readonly Whitelist _whitelist = new(profilo.Whitelist);

        public bool DaAnonimizzare(NomeTrovato nome)
        {
            if (nome.DiSistema) return false;
            if (_blacklist.Contiene(nome.Nome)) return true;
            if (profilo.TrattamentoDi(nome.Ruolo) != Trattamento.Anonimizza) return false;
            return !_whitelist.Contiene(nome.Nome) || mappa.Contiene(nome.Ruolo, nome.Nome);
        }

        /// <summary>Il dominio di <c>DOMINIO\utente</c>: segue la matrice, ma la blacklist vince e i domini di sistema restano.</summary>
        public bool DominioDaAnonimizzare(string dominio) =>
            !ListeBase.DominioDiSistema(dominio)
            && (_blacklist.Contiene(dominio) || profilo.TrattamentoDi(Ruolo.Dominio) == Trattamento.Anonimizza);
    }

    /// <summary>Tipo SQL noto per colonne, variabili e parametri (serve al nome: <c>col_vc50_012</c>).</summary>
    private static string? TipoDi(NomeTrovato nome, Conoscenza conoscenza) =>
        nome.Ruolo is Ruolo.Colonna or Ruolo.Variabile or Ruolo.Parametro
            ? conoscenza.TipoDi(nome.Ruolo, nome.Nome)
            : null;

    // --- nomi composti ---

    /// <summary>Nomi originali di oggetti già anonimizzati con il loro nome anonimo, dal più lungo al più corto.</summary>
    private static IReadOnlyList<(string Originale, string Anonimo)> PartiSostituibili(MappaCliente mappa) =>
        [.. mappa.Voci
            .Where(v => RuoliDelleParti.Contains(v.Ruolo))
            .Select(v => (v.Originale, v.Anonimo))
            .OrderByDescending(v => v.Originale.Length)];

    /// <summary>
    /// Sostituisce nel nome le parti che sono oggetti anonimizzati. Una parte "pulita" è delimitata da underscore o
    /// punteggiatura: <c>IX_Ordini_Data</c> → <c>IX_tbl_001_Data</c>. Restituisce <c>null</c> se una parte è attaccata
    /// ad altro (<c>IXOrdini</c>, <c>IX_OrdiniExtra</c>, <c>IX_Ordini2</c>): sostituendola il nome anonimo si
    /// confonderebbe con ciò che lo circonda e il ripristino non darebbe più il nome esatto, quindi in quel caso va
    /// anonimizzato il nome intero. <c>IX_Ordinine</c> invece non contiene la parte <c>Ordini</c> e resta com'è.
    /// </summary>
    internal static string? SostituisciParti(string nome, IReadOnlyList<(string Originale, string Anonimo)> parti)
    {
        if (parti.Count == 0) return nome;

        var risultato = new System.Text.StringBuilder(nome.Length + 16);
        var i = 0;
        while (i < nome.Length)
        {
            (string Originale, string Anonimo)? trovato = null;
            if (InizioParola(nome, i))
                foreach (var parte in parti)
                    if (i + parte.Originale.Length <= nome.Length
                        && string.Compare(nome, i, parte.Originale, 0, parte.Originale.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && FineParola(nome, i + parte.Originale.Length))
                    {
                        trovato = parte;
                        break;
                    }

            if (trovato is { } t)
            {
                var fine = i + t.Originale.Length;
                var pulita = (i == 0 || !char.IsLetterOrDigit(nome[i - 1]))
                             && (fine >= nome.Length || !char.IsLetterOrDigit(nome[fine]));
                if (!pulita) return null;

                risultato.Append(t.Anonimo);
                i = fine;
            }
            else
            {
                risultato.Append(nome[i]);
                i++;
            }
        }
        return risultato.ToString();
    }

    // Inizio e fine di una parola, anche con CamelCase o cifre: servono a trovare le parti; se non sono
    // delimitate da separatori, SostituisciParti rinuncia (vedi sopra).
    private static bool InizioParola(string nome, int i)
    {
        if (i == 0 || !char.IsLetter(nome[i - 1])) return true;
        if (!char.IsUpper(nome[i])) return false;
        return char.IsLower(nome[i - 1])
               || (char.IsUpper(nome[i - 1]) && i + 1 < nome.Length && char.IsLower(nome[i + 1]));
    }

    private static bool FineParola(string nome, int fine) =>
        fine >= nome.Length || !char.IsLetter(nome[fine]) || char.IsUpper(nome[fine]);

    /// <summary>
    /// Il nome anonimo. Login e utenti nella forma <c>DOMINIO\utente</c> si spezzano: il dominio prende un
    /// <c>dom_01</c>, l'utente un <c>usr_007</c> (<c>dom_01\usr_007</c>).
    /// </summary>
    private static string NomeAnonimo(
        NomeTrovato nome, MappaCliente mappa, Conoscenza conoscenza, Liste liste, Infra infrastruttura)
    {
        // Un server scritto come testo (@datasrc = 'SRVSQL02\ISTANZA1'): host e istanza come in una stringa di connessione.
        if (nome.Ruolo == Ruolo.Server && nome.DaStringa) return infrastruttura.Letterali.AnonimizzaServer(nome.Nome);

        var barra = nome.Nome.IndexOf('\\');
        if (nome.Ruolo is Ruolo.Login or Ruolo.Utente && barra > 0 && barra < nome.Nome.Length - 1)
        {
            var dominio = nome.Nome[..barra];
            if (liste.DominioDaAnonimizzare(dominio)) dominio = mappa.Ottieni(Ruolo.Dominio, dominio);
            return dominio + "\\" + mappa.Ottieni(nome.Ruolo, nome.Nome[(barra + 1)..]);
        }

        return mappa.Ottieni(nome.Ruolo, nome.Nome, TipoDi(nome, conoscenza));
    }

    /// <summary>Rimette le parentesi o gli apici come nell'originale (e, per i nomi dentro una stringa, il prefisso N).</summary>
    private static string Riquota(string nuovo, NomeTrovato nome, string tokenOriginale)
    {
        if (nome.DaStringa)
        {
            var prefisso = tokenOriginale.StartsWith('N') || tokenOriginale.StartsWith('n') ? tokenOriginale[..1] : "";
            return prefisso + "'" + nuovo.Replace("'", "''") + "'";
        }

        return nome.Apici switch
        {
            QuoteType.SquareBracket => "[" + nuovo.Replace("]", "]]") + "]",
            QuoteType.DoubleQuote => "\"" + nuovo.Replace("\"", "\"\"") + "\"",
            _ => nuovo,
        };
    }

    /// <summary>Il segnaposto fisso di una password o di un SID, nello stesso formato del valore originale.</summary>
    private static string TestoSegnaposto(Segreto segreto, string tokenOriginale)
    {
        if (segreto.Tipo == TipoSegreto.Sid) return SidSegnaposto;
        if (tokenOriginale.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return "0x00"; // hash di password
        var prefisso = tokenOriginale.StartsWith('N') || tokenOriginale.StartsWith('n') ? tokenOriginale[..1] : "";
        return prefisso + "'" + PasswordSegnaposto + "'";
    }
}
