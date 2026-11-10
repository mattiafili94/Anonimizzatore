using Anonimizzatore.Motore.Classificazione;

namespace Anonimizzatore.Motore.Test.Classificazione;

public class BlacklistTest
{
    [Theory]
    [InlineData("FK_Clienti_Ordini", "fk,clienti,ordini")]
    [InlineData("IdOrdine", "id,ordine")]
    [InlineData("XMLData2", "xml,data,2")]
    [InlineData("@CodiceAcme", "codice,acme")]
    [InlineData("col_vc50_012", "col,vc,50,012")]
    [InlineData("acme-srl dati", "acme,srl,dati")]
    public void Dividi_in_parole(string nome, string attese) =>
        Assert.Equal(attese.Split(','), Parole.Dividi(nome));

    [Theory]
    [InlineData("Acme", "Acme", true)]
    [InlineData("Acme", "AcmeOrdini", true)]
    [InlineData("Acme", "Cod_Acme", true)]
    [InlineData("Acme", "@CodiceAcme", true)]
    [InlineData("Acme", "ACME", true)]
    [InlineData("acme", "IdAcmeCliente", true)]
    [InlineData("Acme", "Acmeria", false)]        // parola diversa
    [InlineData("Acme", "Macme", false)]
    [InlineData("Acme Srl", "AcmeSrl_Dati", true)]
    [InlineData("Acme Srl", "ACMESRL", true)]
    [InlineData("AcmeSrl", "acme_srl", true)]
    [InlineData("Acme Srl", "Acme", false)]       // la voce ha due parole: serve anche "Srl"
    [InlineData("Acme", "Clienti", false)]
    public void Una_voce_coincide_se_le_sue_parole_stanno_di_seguito_nel_nome(string voce, string nome, bool atteso) =>
        Assert.Equal(atteso, new Blacklist([voce]).Contiene(nome));

    [Fact]
    public void Blacklist_vuota_o_con_voci_vuote_non_coincide_con_nulla()
    {
        Assert.False(new Blacklist([]).Contiene("Acme"));
        Assert.False(new Blacklist(["", "  ", "__"]).Contiene("Acme"));
    }
}
