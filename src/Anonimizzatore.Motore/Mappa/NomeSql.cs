namespace Anonimizzatore.Motore.Mappa;

/// <summary>Regole comuni per confrontare i nomi T-SQL.</summary>
public static class NomeSql
{
    /// <summary>Confronto case-insensitive, come una collation CI di SQL Server.</summary>
    public static StringComparer Confronto => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Chiave di un nome: senza parentesi quadre o doppi apici esterni (con <c>]]</c> e <c>""</c> ripristinati).
    /// <c>[Ordini]</c>, <c>"Ordini"</c> e <c>Ordini</c> hanno la stessa chiave.
    /// </summary>
    public static string Normalizza(string nome)
    {
        ArgumentNullException.ThrowIfNull(nome);
        var n = nome.Trim();
        if (n.Length >= 2 && n[0] == '[' && n[^1] == ']')
            return n[1..^1].Replace("]]", "]");
        if (n.Length >= 2 && n[0] == '"' && n[^1] == '"')
            return n[1..^1].Replace("\"\"", "\"");
        return n;
    }
}
