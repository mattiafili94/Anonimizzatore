using System.Text;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.SchemaDb;

namespace Anonimizzatore.Motore.Legenda;

/// <summary>Il testo da copiare e dare all'IA, e gli script che non ne fanno parte.</summary>
/// <param name="Testo">Legenda, schema anonimizzato (se c'è) e script.</param>
/// <param name="ScriptEsclusi">Posizioni (da 0) degli script che non è stato possibile anonimizzare e quindi non sono nel testo.</param>
public sealed record TestoUnico(string Testo, IReadOnlyList<int> ScriptEsclusi);

/// <summary>
/// Compone il testo unico per l'IA: la <see cref="GeneratoreLegenda">legenda</see>, lo schema anonimizzato delle tabelle citate
/// (solo se è stato importato uno schema) e gli script, separati da intestazioni <c>-- Script 1</c>, <c>-- Script 2</c>...
/// (con un solo script niente intestazione). Uno script non anonimizzabile non entra nel testo: non si copia mai un testo con
/// nomi in chiaro.
/// </summary>
public static class TestoPerIA
{
    public static TestoUnico Componi(
        RisultatoAnonimizzazione risultato, MappaCliente mappa, SchemaDatabase? schema = null, Profilo? profilo = null)
    {
        ArgumentNullException.ThrowIfNull(risultato);
        ArgumentNullException.ThrowIfNull(mappa);
        if (schema is not null && profilo is null)
            throw new ArgumentNullException(nameof(profilo), "Per lo schema anonimizzato serve il profilo (le stesse regole degli script).");

        var inclusi = risultato.Script.Select((s, i) => (Script: s, Indice: i)).Where(x => x.Script.Testo is not null).ToList();
        var esclusi = risultato.Script.Select((s, i) => (s, i)).Where(x => x.s.Testo is null).Select(x => x.i).ToList();

        var corpo = new StringBuilder();
        foreach (var (script, indice) in inclusi)
        {
            if (corpo.Length > 0) corpo.AppendLine().AppendLine();
            if (risultato.Script.Count > 1) corpo.AppendLine($"-- Script {indice + 1}");
            corpo.Append(script.Testo!.TrimEnd('\r', '\n'));
        }

        var testoSchema = schema is null
            ? ""
            : SchemaAnonimizzato.Genera(schema, inclusi.Select(x => x.Script.Testo!), profilo!, mappa);

        var sb = new StringBuilder();
        sb.AppendLine(GeneratoreLegenda.Genera(testoSchema + "\n" + corpo)).AppendLine();
        if (testoSchema.Length > 0)
        {
            sb.AppendLine("-- SCHEMA (anonimizzato) delle tabelle usate dagli script: serve solo come riferimento, non va eseguito.");
            sb.AppendLine(testoSchema).AppendLine();
        }
        sb.AppendLine(corpo.ToString());

        return new TestoUnico(sb.ToString(), esclusi);
    }
}
