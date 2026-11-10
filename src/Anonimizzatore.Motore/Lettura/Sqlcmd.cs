using System.Text.RegularExpressions;

namespace Anonimizzatore.Motore.Lettura;

/// <summary>Un pezzo di script: una riga SQLCMD (<c>:setvar</c>, <c>:connect</c>, <c>!!</c>...) oppure un blocco di T-SQL.</summary>
/// <param name="Testo">Il testo, con i terminatori di riga.</param>
/// <param name="Sqlcmd">Vero se è una riga SQLCMD (una sola riga).</param>
/// <param name="RigaIniziale">Numero (da 1) della prima riga del pezzo nello script originale.</param>
public sealed record SegmentoSqlcmd(string Testo, bool Sqlcmd, int RigaIniziale);

/// <summary>
/// Le righe SQLCMD non sono T-SQL: per ScriptDom sono errori di sintassi. Si separano dal resto prima di leggere lo
/// script, e ogni blocco di T-SQL tra due righe SQLCMD viene letto da solo. Anche <c>GO</c> con conteggio resta T-SQL
/// (lo capisce ScriptDom). Il riconoscimento è prudente: la riga deve iniziare con un comando SQLCMD noto e non stare
/// dentro un commento <c>/* */</c>.
/// </summary>
public static partial class Sqlcmd
{
    // Anche "GO 5" (GO con il numero di ripetizioni): è un comando di sqlcmd/SSMS, non T-SQL, e ScriptDom non lo legge.
    [GeneratedRegex(@"^[ \t]*(?::(?:setvar|connect|r|on|out|error|quit|exit|ed|help|list|listvar|reset|serverlist|xml|perftrace)\b|!!|GO[ \t]+\d+[ \t]*(?:--.*)?(?:\r\n|\r|\n|$))",
        RegexOptions.IgnoreCase)]
    private static partial Regex InizioRigaSqlcmd();

    [GeneratedRegex(@"[^\r\n]*(?:\r\n|\r|\n|$)")]
    private static partial Regex Riga();

    /// <summary>Divide lo script in righe SQLCMD e blocchi di T-SQL, nell'ordine. Rimessi in fila danno il testo originale.</summary>
    public static IReadOnlyList<SegmentoSqlcmd> Dividi(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        var segmenti = new List<SegmentoSqlcmd>();
        var blocco = new System.Text.StringBuilder();
        var rigaBlocco = 1;
        var numero = 0;
        var dentroCommento = 0; // livello di /* */ aperti (T-SQL li annida)

        foreach (Match m in Riga().Matches(testo))
        {
            if (m.Length == 0) continue; // l'ultimo match vuoto in fondo al testo
            numero++;
            var riga = m.Value;

            if (dentroCommento == 0 && InizioRigaSqlcmd().IsMatch(riga))
            {
                if (blocco.Length > 0)
                {
                    segmenti.Add(new SegmentoSqlcmd(blocco.ToString(), false, rigaBlocco));
                    blocco.Clear();
                }
                segmenti.Add(new SegmentoSqlcmd(riga, true, numero));
                continue;
            }

            if (blocco.Length == 0) rigaBlocco = numero;
            blocco.Append(riga);
            dentroCommento = Math.Max(0, dentroCommento + Occorrenze(riga, "/*") - Occorrenze(riga, "*/"));
        }

        if (blocco.Length > 0) segmenti.Add(new SegmentoSqlcmd(blocco.ToString(), false, rigaBlocco));
        return segmenti;
    }

    private static int Occorrenze(string testo, string cosa)
    {
        var n = 0;
        for (var i = testo.IndexOf(cosa, StringComparison.Ordinal); i >= 0; i = testo.IndexOf(cosa, i + cosa.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
