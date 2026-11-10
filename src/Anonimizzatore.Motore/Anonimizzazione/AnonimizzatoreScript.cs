using Anonimizzatore.Motore.Classificazione;
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
/// Punto d'ingresso del motore. Per ogni script: lettura (ScriptDom), classificazione dei nomi, decisione
/// (sistema → in chiaro; poi la matrice del profilo), sostituzione sui token del testo originale.
/// Tutti gli script di una chiamata condividono la stessa mappa.
/// </summary>
/// <remarks>
/// Nome diverso da "Anonimizzatore" apposta: sarebbe uguale al primo pezzo del namespace e creerebbe ambiguità.
/// </remarks>
public static class AnonimizzatoreScript
{
    public static RisultatoAnonimizzazione Anonimizza(IReadOnlyList<string> script, Profilo profilo, MappaCliente mappa)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(profilo);
        ArgumentNullException.ThrowIfNull(mappa);

        var letti = script.Select(LettoreScript.Leggi).ToList();

        // I nomi veri presenti negli script non devono mai coincidere con un nome generato.
        foreach (var letto in letti)
            mappa.AggiungiNomiRiservati(letto.Token
                .Where(t => t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                .Select(t => t.Text));

        // Giro 1 su tutti gli script: i CREATE di uno servono a classificare i riferimenti degli altri.
        var conoscenza = Conoscenza.Da(mappa);
        var pre = new Dictionary<ScriptLetto, PreAnalisi>();
        foreach (var letto in letti.Where(l => l.Valido))
        {
            var p = new PreAnalisi(conoscenza);
            letto.Albero.Accept(p);
            pre[letto] = p;
        }

        // Giro 2: classificazione e sostituzione.
        var risultati = letti.Select(l => Elabora(l, pre.GetValueOrDefault(l), conoscenza, profilo, mappa)).ToList();
        return new RisultatoAnonimizzazione(risultati);
    }

    private static ScriptAnonimizzato Elabora(
        ScriptLetto letto, PreAnalisi? pre, Conoscenza conoscenza, Profilo profilo, MappaCliente mappa)
    {
        if (pre is null)
        {
            // La modalità di riserva (solo token) arriva nella tappa 10: per ora meglio non restituire nulla
            // che potrebbe contenere nomi in chiaro.
            var primo = letto.Errori[0];
            return new ScriptAnonimizzato(null,
                [new Avviso($"Script con errori di sintassi, non anonimizzato: {primo.Message}", primo.Line)]);
        }

        var (nomi, avvisi) = Classificatore.Classifica(letto.Albero, conoscenza, pre);

        var testi = letto.Token.Select(t => t.Text).ToArray();
        foreach (var nome in nomi.Where(n => DaAnonimizzare(n, profilo)))
            testi[nome.IndiceToken] = Riquota(mappa.Ottieni(nome.Ruolo, nome.Nome), nome.Apici);

        return new ScriptAnonimizzato(string.Concat(testi), avvisi);
    }

    private static bool DaAnonimizzare(NomeTrovato nome, Profilo profilo) =>
        !nome.DiSistema && profilo.TrattamentoDi(nome.Ruolo) == Trattamento.Anonimizza;

    /// <summary>Rimette le parentesi o gli apici come nell'originale.</summary>
    private static string Riquota(string nome, QuoteType apici) => apici switch
    {
        QuoteType.SquareBracket => "[" + nome.Replace("]", "]]") + "]",
        QuoteType.DoubleQuote => "\"" + nome.Replace("\"", "\"\"") + "\"",
        _ => nome,
    };
}
