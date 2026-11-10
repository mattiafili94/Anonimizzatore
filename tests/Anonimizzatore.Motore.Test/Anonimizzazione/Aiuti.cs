using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

internal static partial class Aiuti
{
    public static string LeggiEsempio(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Esempi", nome));

    /// <summary>Anonimizza un solo script con un profilo predefinito e una mappa nuova (se non passati).</summary>
    public static string Anon(string sql, MappaCliente? mappa = null, Profilo? profilo = null)
    {
        var esito = AnonimizzatoreScript.Anonimizza([sql], profilo ?? NuovoProfilo(), mappa ?? new MappaCliente()).Script[0];
        Assert.True(esito.Anonimizzato, string.Join("; ", esito.Avvisi.Select(a => a.Messaggio)));
        return esito.Testo!;
    }

    public static Profilo NuovoProfilo() => new() { Nome = "Prova" };

    [GeneratedRegex(@"(?<![\w@#])(?:#{1,2}tmp|srv|db|sch|dom|tbl|vw|usp|fn|trg|usr|rol|job|ix|pk|fk|df|ck|uq)_\d+(?!\w)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NomeAnonimo();

    /// <summary>
    /// Ripristino SEMPLICE solo per i test di andata e ritorno. Il vero ripristino sul testo libero dell'IA è il
    /// <c>DeAnonimizzatore</c> (tappa 12), che è un'altra cosa.
    /// </summary>
    public static string RipristinaPerProva(string testo, MappaCliente mappa) =>
        NomeAnonimo().Replace(testo, m => mappa.TrovaOriginale(m.Value)?.Originale ?? m.Value);

    /// <summary>Gli identificatori (senza parentesi) che compaiono nel testo, come li vede ScriptDom.</summary>
    public static HashSet<string> Identificatori(string sql)
    {
        var letto = LettoreScript.Leggi(sql);
        Assert.True(letto.Valido, "Il testo non è T-SQL valido: " + string.Join("; ", letto.Errori.Select(e => e.Message)));
        return letto.Token
            .Where(t => t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
            .Select(t => NomeSql.Normalizza(t.Text))
            .ToHashSet(NomeSql.Confronto);
    }
}
