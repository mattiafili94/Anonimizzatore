using System.Text.RegularExpressions;

namespace Anonimizzatore.Motore.Mappa;

/// <summary>
/// Trasforma un tipo SQL nel codice breve usato nei nomi anonimi:
/// <c>varchar(50)</c> → <c>vc50</c>, <c>decimal(18,2)</c> → <c>dec18_2</c>, tipo ignoto → <c>x</c>.
/// </summary>
public static partial class CodiceTipo
{
    /// <summary>Codice usato quando il tipo non è noto.</summary>
    public const string Ignoto = "x";

    private static readonly Dictionary<string, string> Sigle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["varchar"] = "vc", ["nvarchar"] = "nvc", ["char"] = "ch", ["nchar"] = "nch",
        ["varbinary"] = "vb", ["binary"] = "bin",
        ["decimal"] = "dec", ["numeric"] = "num",
        ["int"] = "int", ["bigint"] = "bint", ["smallint"] = "sint", ["tinyint"] = "tint",
        ["bit"] = "bit", ["money"] = "money", ["smallmoney"] = "smoney",
        ["float"] = "flt", ["real"] = "real",
        ["datetime"] = "dt", ["datetime2"] = "dt2", ["smalldatetime"] = "sdt", ["datetimeoffset"] = "dto",
        ["date"] = "d", ["time"] = "tm",
        ["uniqueidentifier"] = "guid", ["xml"] = "xml", ["text"] = "txt", ["ntext"] = "ntxt", ["image"] = "img",
        ["sql_variant"] = "var", ["sysname"] = "sysname", ["rowversion"] = "rv", ["timestamp"] = "rv",
        ["hierarchyid"] = "hid", ["geography"] = "geog", ["geometry"] = "geom", ["json"] = "json",
    };

    // Tipi per cui la lunghezza tra parentesi fa parte del codice (vc50, nvcmax, dec18_2).
    private static readonly HashSet<string> ConLunghezza = new(StringComparer.OrdinalIgnoreCase)
    {
        "varchar", "nvarchar", "char", "nchar", "varbinary", "binary", "decimal", "numeric",
    };

    [GeneratedRegex(@"^\s*\[?(?<nome>[A-Za-z_0-9]+)\]?\s*(\(\s*(?<p1>max|\d+)\s*(,\s*(?<p2>\d+)\s*)?\))?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex FormaTipo();

    /// <summary>Calcola il codice. <paramref name="tipoSql"/> nullo, vuoto o sconosciuto → <see cref="Ignoto"/>.</summary>
    public static string Da(string? tipoSql)
    {
        if (string.IsNullOrWhiteSpace(tipoSql)) return Ignoto;

        var m = FormaTipo().Match(tipoSql);
        if (!m.Success) return Ignoto;

        var nome = m.Groups["nome"].Value;
        if (!Sigle.TryGetValue(nome, out var sigla)) return Ignoto;
        if (!ConLunghezza.Contains(nome)) return sigla;

        var p1 = m.Groups["p1"].Value.ToLowerInvariant();
        var p2 = m.Groups["p2"].Value;
        if (p1.Length == 0) return sigla;
        return p2.Length == 0 ? $"{sigla}{p1}" : $"{sigla}{p1}_{p2}";
    }

    // Dalla sigla al nome del tipo ("vc" → varchar). Con due tipi per la stessa sigla vale il primo (rowversion/timestamp).
    private static readonly Dictionary<string, string> SigleInverse = Sigle
        .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<sigla>[a-z]+)(?<p1>max|\d+)(?:_(?<p2>\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex FormaCodice();

    /// <summary>
    /// L'inverso di <see cref="Da"/>, per la legenda: <c>vc50</c> → <c>varchar(50)</c>, <c>dec18_2</c> → <c>decimal(18,2)</c>,
    /// <c>int</c> → <c>int</c>. <c>null</c> se il codice è <see cref="Ignoto"/> o non si riconosce.
    /// </summary>
    public static string? Descrivi(string codice)
    {
        if (string.IsNullOrWhiteSpace(codice) || codice.Equals(Ignoto, StringComparison.OrdinalIgnoreCase)) return null;
        if (SigleInverse.TryGetValue(codice, out var semplice)) return semplice;

        var m = FormaCodice().Match(codice);
        if (!m.Success || !SigleInverse.TryGetValue(m.Groups["sigla"].Value, out var nome) || !ConLunghezza.Contains(nome)) return null;

        var p2 = m.Groups["p2"].Success ? "," + m.Groups["p2"].Value : "";
        return $"{nome}({m.Groups["p1"].Value.ToLowerInvariant()}{p2})";
    }
}
