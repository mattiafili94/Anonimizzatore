using Anonimizzatore.Motore.Lettura;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Test.Lettura;

public class LettoreScriptTest
{
    private static string LeggiEsempio(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Esempi", nome));

    [Fact]
    public void Script_valido_viene_letto_senza_errori_e_diviso_in_batch()
    {
        var letto = LettoreScript.Leggi(LeggiEsempio("vendite-ordini.sql"));

        Assert.True(letto.Valido, string.Join("; ", letto.Errori.Select(e => $"riga {e.Line}: {e.Message}")));
        Assert.Equal(3, letto.NumeroBatch); // USE / CREATE TABLE / CREATE PROCEDURE
    }

    [Fact]
    public void I_token_ricompongono_esattamente_il_testo_originale()
    {
        // Base del principio "sostituzione sui token del testo originale": nulla si perde (spazi, a capo, commenti).
        var testo = LeggiEsempio("vendite-ordini.sql");

        var letto = LettoreScript.Leggi(testo);

        Assert.Equal(testo, letto.RicomponiDaiToken());
    }

    [Fact]
    public void I_commenti_sono_token_riconoscibili()
    {
        var letto = LettoreScript.Leggi(LeggiEsempio("vendite-ordini.sql"));

        var commenti = letto.Token
            .Where(t => t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            .ToList();

        Assert.Equal(3, commenti.Count);
    }

    [Fact]
    public void Script_con_errori_restituisce_gli_errori_senza_eccezioni()
    {
        var letto = LettoreScript.Leggi("SELECT * FORM dbo.Clienti WHERE");

        Assert.False(letto.Valido);
        Assert.NotEmpty(letto.Errori);
    }
}
