namespace Anonimizzatore.Motore.Mappa;

/// <summary>
/// Costruisce i nomi parlanti: prefisso del ruolo + (tipo) + numero.
/// <c>tbl_001</c>, <c>srv_01</c>, <c>col_vc50_012</c>, <c>@p_int_004</c>, <c>#tmp_003</c>.
/// </summary>
public static class GeneratoreNomi
{
    // Famiglie con pochi elementi: numero a 2 cifre (srv_01). Le altre a 3 cifre (tbl_001).
    private static readonly HashSet<string> DueCifre = ["srv", "db", "sch", "dom", "ag"];

    /// <param name="ruolo">Ruolo del nome.</param>
    /// <param name="numero">Numero progressivo nella famiglia (da 1).</param>
    /// <param name="codiceTipo">Solo per colonne e variabili: codice di <see cref="CodiceTipo"/>.</param>
    /// <param name="nomeOriginale">Solo per le temporanee: serve a conservare <c>#</c> o <c>##</c>.</param>
    public static string Formatta(Ruolo ruolo, int numero, string? codiceTipo = null, string? nomeOriginale = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numero, 1);
        var famiglia = FamigliaRuolo.Di(ruolo);
        var n = numero.ToString(DueCifre.Contains(famiglia) ? "D2" : "D3");
        var tipo = string.IsNullOrEmpty(codiceTipo) ? CodiceTipo.Ignoto : codiceTipo;

        return ruolo switch
        {
            Ruolo.Colonna => $"col_{tipo}_{n}",
            Ruolo.Variabile => $"@v_{tipo}_{n}",
            Ruolo.Parametro => $"@p_{tipo}_{n}",
            Ruolo.Temporanea => (nomeOriginale?.StartsWith("##") == true ? "##" : "#") + $"tmp_{n}",
            Ruolo.Email => $"mail_{n}@example.invalid",
            _ => $"{famiglia}_{n}",
        };
    }
}
