using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Lettura;

/// <summary>Risultato della lettura di uno script: testo originale, albero sintattico, token ed eventuali errori.</summary>
/// <param name="TestoOriginale">Il testo così come è stato incollato.</param>
/// <param name="Albero">Albero sintattico di ScriptDom: dice cosa rappresenta ogni pezzo dello script.</param>
/// <param name="Token">Tutti i token in ordine (parole, simboli, stringhe, commenti, spazi): ricomposti danno il testo originale.</param>
/// <param name="Errori">Errori di sintassi trovati; vuoto se lo script è valido.</param>
public sealed record ScriptLetto(
    string TestoOriginale,
    TSqlFragment Albero,
    IList<TSqlParserToken> Token,
    IList<ParseError> Errori)
{
    /// <summary>Vero se lo script non ha errori di sintassi.</summary>
    public bool Valido => Errori.Count == 0;

    /// <summary>Numero di batch (parti separate da GO).</summary>
    public int NumeroBatch => Albero is TSqlScript script ? script.Batches.Count : 0;

    /// <summary>Ricompone il testo concatenando i token: deve coincidere con <see cref="TestoOriginale"/>.</summary>
    public string RicomponiDaiToken() => string.Concat(Token.Select(t => t.Text));
}
