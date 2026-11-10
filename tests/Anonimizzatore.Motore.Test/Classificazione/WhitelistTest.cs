using Anonimizzatore.Motore.Classificazione;

namespace Anonimizzatore.Motore.Test.Classificazione;

public class WhitelistTest
{
    [Theory]
    [InlineData("Data")]
    [InlineData("IdData")]
    [InlineData("usp_GetData")]
    [InlineData("uspCalcolaTotale")]
    [InlineData("Log2024")]            // le cifre contano come generiche
    [InlineData("[Storico]")]
    [InlineData("STORICO_LOG")]
    public void Nomi_con_sole_parole_generiche_restano_in_chiaro(string nome) =>
        Assert.True(new Whitelist([]).Contiene(nome));

    [Theory]
    [InlineData("Ordini")]
    [InlineData("usp_GetOrdini")]      // una parola di dominio basta
    [InlineData("CodiceFiscale")]      // "fiscale" non è generico
    [InlineData("Acme")]
    [InlineData("#")]                  // nessuna parola
    public void Nomi_con_una_parola_di_dominio_non_sono_in_whitelist(string nome) =>
        Assert.False(new Whitelist([]).Contiene(nome));

    [Fact]
    public void Voce_a_una_parola_del_cliente_vale_come_nome_intero_e_come_parola_generica()
    {
        var w = new Whitelist(["Ordini"]);

        Assert.True(w.Contiene("Ordini"));
        Assert.True(w.Contiene("usp_GetOrdini"));
        Assert.True(w.Contiene("IX_Ordini_Data"));
        Assert.False(w.Contiene("OrdiniAperti_Acme"));
    }

    [Fact]
    public void Voce_di_piu_parole_vale_solo_per_nome_intero()
    {
        var w = new Whitelist(["OrdiniAperti", "Servizio Backup", "[Report Mensile]"]);

        Assert.True(w.Contiene("OrdiniAperti"));
        Assert.True(w.Contiene("ordiniaperti"));
        Assert.True(w.Contiene("Servizio Backup"));
        Assert.True(w.Contiene("Report Mensile"));
        Assert.False(w.Contiene("Ordini"));    // le sue parole NON diventano generiche
        Assert.False(w.Contiene("Aperti"));
        Assert.False(w.Contiene("Servizio"));
    }

    [Fact]
    public void Voci_vuote_sono_ignorate()
    {
        var w = new Whitelist(["", "   ", "__"]);

        Assert.False(w.Contiene("Ordini"));
    }
}
