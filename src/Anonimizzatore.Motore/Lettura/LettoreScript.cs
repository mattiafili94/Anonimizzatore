using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Lettura;

/// <summary>
/// Passo 1 del motore: trasforma il testo di uno script T-SQL in token e albero sintattico, usando ScriptDom.
/// </summary>
public static class LettoreScript
{
    /// <summary>Legge uno script. Non solleva eccezioni sugli errori di sintassi: li restituisce in <see cref="ScriptLetto.Errori"/>.</summary>
    public static ScriptLetto Leggi(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        // TSql170Parser = sintassi di SQL Server 2025, che comprende anche quella delle versioni precedenti.
        // initialQuotedIdentifiers: true = comportamento predefinito di SQL Server ("..." sono nomi, non stringhe).
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);

        try
        {
            using var lettore = new StringReader(testo);
            var albero = parser.Parse(lettore, out IList<ParseError> errori);
            return new ScriptLetto(testo, albero, albero.ScriptTokenStream, errori);
        }
        catch (Exception eccezione) when (eccezione is not OutOfMemoryException)
        {
            // Il parser di Microsoft può andare in errore interno su script insoliti: non deve far cadere il motore.
            // Si restituisce uno script "non valido" con un errore, come per un errore di sintassi qualsiasi.
            return new ScriptLetto(testo, new TSqlScript(), TokenOVuoti(testo),
                [new ParseError(0, 0, 1, 1, $"Errore interno del parser ({eccezione.GetType().Name}): lo script non è stato letto.")]);
        }
    }

    private static IList<TSqlParserToken> TokenOVuoti(string testo)
    {
        try
        {
            using var lettore = new StringReader(testo);
            return new TSql170Parser(initialQuotedIdentifiers: true).GetTokenStream(lettore, out _);
        }
        catch (Exception)
        {
            return [];
        }
    }
}
