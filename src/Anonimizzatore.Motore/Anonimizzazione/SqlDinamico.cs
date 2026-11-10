using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Anonimizzazione;

/// <summary>Una stringa dello script che contiene T-SQL (o la dichiarazione dei parametri di <c>sp_executesql</c>).</summary>
/// <param name="Token">Posizione del letterale stringa nello script che la contiene.</param>
/// <param name="Letto">Il contenuto della stringa, già letto da ScriptDom (con gli apici raddoppiati tolti).</param>
/// <param name="Prefisso">Testo aggiunto davanti per poterlo leggere (vuoto per il T-SQL vero): va tolto dopo.</param>
/// <param name="Suffisso">Testo aggiunto in fondo: va tolto dopo.</param>
internal sealed record FiglioSql(int Token, ScriptLetto Letto, string Prefisso, string Suffisso);

/// <summary>
/// SQL dinamico: <c>EXEC('...')</c>, <c>sp_executesql N'...'</c>, <c>@command</c> di <c>sp_add_jobstep</c> e qualunque stringa
/// che contenga T-SQL valido. Una stringa così si analizza come uno script a sé (in modo ricorsivo: dentro può
/// esserci altro SQL dinamico). Le stringhe che iniziano come SQL ma non sono valide (pezzi da concatenare, errori)
/// non si possono analizzare: lì si sostituiscono per dizionario i nomi già noti, con un avviso.
/// </summary>
internal static partial class SqlDinamico
{
    /// <summary>Quanti livelli di SQL dentro SQL si seguono (una stringa che contiene una stringa che contiene...).</summary>
    public const int ProfonditaMassima = 8;

    // Con quale parola può iniziare un'istruzione: serve a non provare a leggere come T-SQL ogni stringa dello script.
    private static readonly HashSet<string> InizioIstruzione = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "merge", "exec", "execute", "create", "alter", "drop", "truncate",
        "declare", "set", "with", "use", "backup", "restore", "grant", "deny", "revoke", "if", "begin", "while",
        "print", "raiserror", "throw", "dbcc", "bulk", "open", "close", "fetch", "deallocate", "kill", "checkpoint",
        "waitfor", "return", "goto", "commit", "rollback", "save", "enable", "disable", "rename", "update",
    };

    // Parole che fanno pensare a un pezzo di SQL costruito a mano, anche se non è valido da solo: le istruzioni più
    // tipiche e le clausole (' FROM ', ' WHERE ', ' JOIN '...). Più strette dell'elenco sopra, per non scambiare
    // per SQL un messaggio come 'Save completed'.
    private static readonly HashSet<string> InizioFrammentoSql = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "merge", "exec", "execute", "create", "alter", "drop", "truncate",
        "declare", "with", "backup", "restore", "grant", "deny", "revoke",
        "from", "where", "join", "inner", "left", "right", "full", "cross", "outer", "on", "and", "or", "order",
        "group", "having", "union", "values", "into", "output", "option",
    };

    private const string PrefissoParametri = "CREATE PROCEDURE sys.__anonimizzatore__ ";
    private const string SuffissoParametri = " AS SELECT 1";

    [GeneratedRegex(@"^\s*(?:(?:--[^\r\n]*(?:\r\n|\r|\n)|/\*.*?\*/)\s*)*(?<p>@?[A-Za-z_]+)", RegexOptions.Singleline)]
    private static partial Regex PrimaParolaRegex();

    /// <summary>Divide un letterale stringa nel prefisso (<c>N</c> o vuoto) e nel contenuto, con gli apici raddoppiati tolti.</summary>
    public static (string Prefisso, string Contenuto)? Spacchetta(string testoToken)
    {
        var prefisso = testoToken.StartsWith('N') || testoToken.StartsWith('n') ? testoToken[..1] : "";
        if (testoToken.Length < prefisso.Length + 2 || testoToken[prefisso.Length] != '\'' || !testoToken.EndsWith('\''))
            return null;
        return (prefisso, testoToken[(prefisso.Length + 1)..^1].Replace("''", "'"));
    }

    /// <summary>Rimette il contenuto tra apici (raddoppiandoli) con il prefisso <c>N</c> se c'era.</summary>
    public static string Ricompatta(string prefisso, string contenuto) => prefisso + "'" + contenuto.Replace("'", "''") + "'";

    /// <summary>La prima parola del testo, saltando spazi e commenti (le variabili iniziano con <c>@</c>).</summary>
    private static string? PrimaParola(string contenuto)
    {
        var m = PrimaParolaRegex().Match(contenuto);
        return m.Success ? m.Groups["p"].Value : null;
    }

    // Una stringa che è soltanto un nome di oggetto, anche con più parti: 'Ordini', N'dbo.Ordini', '[Vendite].[dbo].[Ordini]'.
    [GeneratedRegex(@"^\s*(?:\[[^\]\r\n]+\]|[A-Za-z_#@][\w$#@]*)(?:\s*\.\s*(?:\[[^\]\r\n]+\]|[A-Za-z_#@][\w$#@]*)){0,3}\s*$")]
    private static partial Regex SoloNomeOggettoRegex();

    /// <summary>
    /// Vero se la stringa contiene soltanto un nome di oggetto (<c>N'dbo.Ordini'</c>): è il caso di
    /// <c>OBJECT_ID('dbo.Ordini')</c>, <c>COL_LENGTH('dbo.Ordini', 'Codice')</c> e di <c>@tabella = N'Ordini'</c> che poi
    /// finisce in un SQL costruito a pezzi.
    /// </summary>
    public static bool EUnNomeOggetto(string contenuto) => SoloNomeOggettoRegex().IsMatch(contenuto);

    /// <summary>Vero se la stringa inizia come un pezzo di SQL (un'istruzione o una clausola): <c>SELECT ...</c>, <c> WHERE x = </c>.</summary>
    public static bool InizioFrammento(string contenuto) =>
        PrimaParola(contenuto) is { } p && InizioFrammentoSql.Contains(p);

    /// <summary>Le stringhe di uno script che sono T-SQL valido o dichiarazioni di parametri.</summary>
    public static IEnumerable<FiglioSql> TrovaFigli(ScriptLetto letto)
    {
        for (var i = 0; i < letto.Token.Count; i++)
        {
            var token = letto.Token[i];
            if (token.TokenType is not (TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral)) continue;
            if (Spacchetta(token.Text) is not { } stringa || stringa.Contenuto.Trim().Length < 4) continue;
            if (PrimaParola(stringa.Contenuto) is not { } prima) continue;

            if (prima.StartsWith('@'))
            {
                // sp_executesql N'SELECT ...', N'@p int, @q varchar(20)', @p = 1: la dichiarazione dei parametri.
                var avvolto = LettoreScript.Leggi(PrefissoParametri + stringa.Contenuto + SuffissoParametri);
                if (avvolto.Valido) yield return new FiglioSql(i, avvolto, PrefissoParametri, SuffissoParametri);
            }
            else if (InizioIstruzione.Contains(prima))
            {
                var letto2 = LettoreScript.Leggi(stringa.Contenuto);
                if (letto2.Valido && HaIstruzioni(letto2)) yield return new FiglioSql(i, letto2, "", "");
            }
        }
    }

    private static bool HaIstruzioni(ScriptLetto letto) =>
        letto.Albero is TSqlScript script && script.Batches.Any(b => b.Statements.Count > 0);

    /// <summary>
    /// Sostituisce nel testo i nomi già in mappa (tabelle, colonne, schemi, database, server, utenti...) con il loro nome
    /// anonimo. Serve dove il SQL è costruito a pezzi e non si può leggere come T-SQL. Confronta senza maiuscole e
    /// minuscole, solo parole intere, anche tra parentesi quadre; una variabile (<c>@x</c>) non è mai un oggetto con lo stesso nome.
    /// </summary>
    public sealed class Dizionario
    {
        private readonly Dictionary<string, string> _anonimi = new(NomeSql.Confronto);
        private readonly Regex? _regex;

        public Dizionario(MappaCliente mappa)
        {
            foreach (var voce in mappa.Voci)
            {
                if (voce.Originale.Length < 2 || voce.Originale.Contains('\\') || voce.Originale.Contains(']')) continue;
                _anonimi.TryAdd(voce.Originale, voce.Anonimo);
            }
            if (_anonimi.Count == 0) return;

            var alternative = string.Join("|", _anonimi.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            _regex = new Regex(
                @"(?<![\w@#$])(?:\[(?<b>" + alternative + @")\]|(?<n>" + alternative + @"))(?![\w$])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Il nome anonimo di un nome già in mappa, o <c>null</c>.</summary>
        public string? Cerca(string nome) => _anonimi.TryGetValue(nome, out var anonimo) ? anonimo : null;

        public string Sostituisci(string testo)
        {
            if (_regex is null) return testo;
            var sb = new StringBuilder(testo.Length);
            var ultimo = 0;
            foreach (Match m in _regex.Matches(testo))
            {
                var tra = m.Groups["b"].Success;
                var anonimo = _anonimi[(tra ? m.Groups["b"] : m.Groups["n"]).Value];
                sb.Append(testo, ultimo, m.Index - ultimo).Append(tra ? "[" + anonimo + "]" : anonimo);
                ultimo = m.Index + m.Length;
            }
            return sb.Append(testo, ultimo, testo.Length - ultimo).ToString();
        }
    }
}
