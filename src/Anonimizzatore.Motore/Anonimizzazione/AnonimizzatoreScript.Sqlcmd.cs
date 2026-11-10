using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Anonimizzazione;

// Righe SQLCMD (:setvar, :connect, :r, !!) e righe di comando che usano sqlcmd/osql/bcp.
public static partial class AnonimizzatoreScript
{
    [GeneratedRegex(@"^(?<ind>[ \t]*)(?<cmd>:[A-Za-z]+|!!)(?<resto>.*)$", RegexOptions.Singleline)]
    private static partial Regex RigaSqlcmd();

    [GeneratedRegex(@"^(?<sp>[ \t]+)(?<nome>\S+)(?:(?<sp2>[ \t]+)(?<valore>.*?))?(?<fine>[ \t]*)$", RegexOptions.Singleline)]
    private static partial Regex ArgomentiSetvar();

    [GeneratedRegex(@"^(?<sp>[ \t]+)(?<server>""[^""]*""|\S+)(?<altro>.*)$", RegexOptions.Singleline)]
    private static partial Regex ArgomentiConnect();

    [GeneratedRegex(@"^(?<sp>[ \t]+)(?<arg>""[^""]*""|\S.*?)(?<fine>[ \t]*)$", RegexOptions.Singleline)]
    private static partial Regex ArgomentoFile();

    // Le opzioni di sqlcmd / osql / bcp. Maiuscole e minuscole contano: -S è il server, -s il separatore.
    [GeneratedRegex(@"(?<![\w-])-(?<f>[SUPdQio])(?<sp>[ \t]*)(?<v>""[^""]*""|[^\s""]+)", RegexOptions.CultureInvariant)]
    private static partial Regex OpzioniComando();

    [GeneratedRegex(@"\b(?:sqlcmd|osql|isql|bcp)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StrumentoSql();

    /// <summary>
    /// Riscrive una riga SQLCMD. Il comando e le variabili <c>$(...)</c> restano; si anonimizzano i valori che sono nomi:
    /// <c>:setvar</c> (in base al nome della variabile), <c>:connect</c> (server, <c>-U</c> utente, <c>-P</c> password),
    /// <c>:r</c>/<c>:out</c>/<c>:error</c> (file), <c>!!</c> (riga di comando).
    /// </summary>
    private static (string Testo, List<Avviso> Avvisi) AnonimizzaRigaSqlcmd(string riga, int numeroRiga, Contesto c)
    {
        var avvisi = new List<Avviso>();
        var terminatore = riga.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : riga.EndsWith('\n') ? "\n" : riga.EndsWith('\r') ? "\r" : "";
        var corpo = riga[..^terminatore.Length];

        var m = RigaSqlcmd().Match(corpo);
        if (!m.Success) return (riga, avvisi);
        var intestazione = m.Groups["ind"].Value + m.Groups["cmd"].Value;
        var resto = m.Groups["resto"].Value;

        string nuovoResto;
        switch (m.Groups["cmd"].Value.ToLowerInvariant())
        {
            case ":setvar":
                nuovoResto = ArgomentiSetvar().Replace(resto, a => !a.Groups["valore"].Success || a.Groups["valore"].Value.Length == 0
                    ? a.Value
                    : a.Groups["sp"].Value + a.Groups["nome"].Value + a.Groups["sp2"].Value
                      + ValoreSetvar(a.Groups["nome"].Value, a.Groups["valore"].Value, c, avvisi, numeroRiga) + a.Groups["fine"].Value);
                break;

            case ":connect":
                nuovoResto = ArgomentiConnect().Replace(resto, a => a.Groups["sp"].Value
                    + SenzaApici(a.Groups["server"].Value, v => NomeSqlcmd(Ruolo.Server, v, c))
                    + AnonimizzaOpzioniComando(a.Groups["altro"].Value, c, avvisi, numeroRiga));
                break;

            case ":r" or ":out" or ":error" or ":ed":
                nuovoResto = ArgomentoFile().Replace(resto, a => a.Groups["sp"].Value
                    + SenzaApici(a.Groups["arg"].Value, v => AnonimizzaFile(v, c)) + a.Groups["fine"].Value);
                break;

            case "!!": // riga di comando della shell
                nuovoResto = c.Infra.TestoLibero.Anonimizza(AnonimizzaOpzioniComando(resto, c, avvisi, numeroRiga)).Testo;
                break;

            default: // :on error, :quit, :exit... non contengono nomi
                nuovoResto = resto;
                break;
        }

        return (intestazione + nuovoResto + terminatore, avvisi);
    }

    /// <summary>
    /// Il valore di <c>:setvar NOME valore</c>. Il nome della variabile dice che cosa contiene: password (cancellata),
    /// percorso, database, server, schema, utente, job. Altrimenti si cercano percorsi, IP, email... e i nomi già noti.
    /// </summary>
    private static string ValoreSetvar(string nome, string valore, Contesto c, List<Avviso> avvisi, int riga)
    {
        if (valore.Contains("$(", StringComparison.Ordinal)) return valore;

        var n = nome.ToLowerInvariant();
        bool Contiene(params string[] parole) => parole.Any(n.Contains);

        return SenzaApici(valore, v =>
        {
            if (Contiene("pass", "pwd", "secret"))
            {
                avvisi.Add(new Avviso($"Variabile SQLCMD {nome} con una password: sostituita da {PasswordSegnaposto} e non salvata.", riga));
                return PasswordSegnaposto;
            }
            if (Contiene("path", "dir", "folder", "cartella", "percorso", "file")) return c.Infra.TestoLibero.Anonimizza(v).Testo;
            if (Contiene("database", "dbname", "db", "catalog")) return NomeSqlcmd(Ruolo.Database, v, c);
            if (Contiene("server", "srv", "instance", "istanza", "host", "node")) return NomeSqlcmd(Ruolo.Server, v, c);
            if (Contiene("schema")) return NomeSqlcmd(Ruolo.Schema, v, c);
            if (Contiene("user", "login", "utente", "account", "owner")) return NomeSqlcmd(Ruolo.Login, v, c);
            if (Contiene("job")) return NomeSqlcmd(Ruolo.Job, v, c);

            // Non si capisce che cosa sia: percorsi, IP, email... e i nomi già noti.
            var infra = c.Infra.Letterali.Anonimizza(v).Testo;
            return infra != v ? infra : c.Dizionario.Sostituisci(v);
        });
    }

    /// <summary>Un file di <c>:r</c>/<c>:out</c>: un percorso, oppure solo il nome di un file con la sua estensione.</summary>
    private static string AnonimizzaFile(string file, Contesto c)
    {
        if (file.Contains("$(", StringComparison.Ordinal) && !file.Contains(':') && !file.StartsWith(@"\\", StringComparison.Ordinal))
            return file;

        var viaInfra = c.Infra.TestoLibero.Anonimizza(file).Testo;
        if (viaInfra != file) return viaInfra;

        var estensione = Path.GetExtension(file);
        if (file.Contains('\\') || file.Contains('/') || estensione.Length == 0) return file;
        return NomeSqlcmd(Ruolo.File, Path.GetFileNameWithoutExtension(file), c) + estensione;
    }

    /// <summary>
    /// Le opzioni di <c>sqlcmd</c>, <c>osql</c> e <c>bcp</c> (anche dopo <c>:connect</c> e <c>!!</c>): <c>-S</c> server,
    /// <c>-U</c> utente, <c>-P</c> password (cancellata), <c>-d</c> database, <c>-Q</c> query, <c>-i</c>/<c>-o</c> file.
    /// </summary>
    private static string AnonimizzaOpzioniComando(string comando, Contesto c, List<Avviso> avvisi, int riga) =>
        OpzioniComando().Replace(comando, m =>
        {
            var f = m.Groups["f"].Value;
            var nuovo = SenzaApici(m.Groups["v"].Value, v => f switch
            {
                "S" => NomeSqlcmd(Ruolo.Server, v, c),
                "U" => NomeSqlcmd(Ruolo.Login, v, c),
                "P" => SegnalaPassword(avvisi, riga),
                "d" => NomeSqlcmd(Ruolo.Database, v, c),
                "Q" => c.Dizionario.Sostituisci(v),
                _ => c.Infra.TestoLibero.Anonimizza(v).Testo, // -i / -o: file
            });
            return "-" + f + m.Groups["sp"].Value + nuovo;
        });

    private static string SegnalaPassword(List<Avviso> avvisi, int riga)
    {
        var avviso = new Avviso($"Riga di comando con una password (-P): sostituita da {PasswordSegnaposto} e non salvata.", riga);
        if (!avvisi.Contains(avviso)) avvisi.Add(avviso);
        return PasswordSegnaposto;
    }

    /// <summary>Applica <paramref name="trasforma"/> al valore senza i doppi apici esterni (se ci sono) e li rimette.</summary>
    private static string SenzaApici(string valore, Func<string, string> trasforma) =>
        valore.Length >= 2 && valore[0] == '"' && valore[^1] == '"'
            ? "\"" + trasforma(valore[1..^1]) + "\""
            : trasforma(valore);

    /// <summary>Un nome (server, database, utente...) trovato fuori dal T-SQL, trattato come in uno script: stesse regole di sistema, matrice, blacklist e whitelist.</summary>
    private static string NomeSqlcmd(Ruolo ruolo, string valore, Contesto c)
    {
        if (valore.Length == 0 || valore.Contains("$(", StringComparison.Ordinal)) return valore;

        var sistema = ruolo switch
        {
            Ruolo.Database => ListeBase.DatabaseDiSistema(valore),
            Ruolo.Server => ListeBase.ServerLocale(valore),
            Ruolo.Login or Ruolo.Utente => ListeBase.PrincipaleDiSistema(valore),
            Ruolo.Schema => ListeBase.SchemaPredefinito(valore),
            _ => false,
        };
        // DaStringa: un server si scrive come in una stringa di connessione (host\istanza,porta).
        var nome = new NomeTrovato(0, ruolo, valore, QuoteType.NotQuoted, sistema, DaStringa: true);
        return c.Liste.DaAnonimizzare(nome) ? NomeAnonimo(nome, c.Mappa, c.Conoscenza, c.Liste, c.Infra) : valore;
    }
}
