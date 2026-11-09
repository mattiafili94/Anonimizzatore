using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Mappa;

public class GeneratoreNomiTest
{
    [Theory]
    [InlineData("varchar(50)", "vc50")]
    [InlineData("NVARCHAR(MAX)", "nvcmax")]
    [InlineData("decimal(18,2)", "dec18_2")]
    [InlineData("decimal( 18 , 2 )", "dec18_2")]
    [InlineData("[int]", "int")]
    [InlineData("datetime", "dt")]
    [InlineData("datetime2(0)", "dt2")]
    [InlineData("bit", "bit")]
    [InlineData("char", "ch")]
    [InlineData("TipoUtenteInventato", "x")]
    [InlineData("", "x")]
    [InlineData(null, "x")]
    public void Codice_del_tipo(string? tipo, string atteso) =>
        Assert.Equal(atteso, CodiceTipo.Da(tipo));

    [Theory]
    [InlineData(Ruolo.Tabella, 1, null, "tbl_001")]
    [InlineData(Ruolo.Vista, 12, null, "vw_012")]
    [InlineData(Ruolo.Procedura, 3, null, "usp_003")]
    [InlineData(Ruolo.Server, 1, null, "srv_01")]
    [InlineData(Ruolo.LinkedServer, 2, null, "srv_02")]
    [InlineData(Ruolo.Database, 1, null, "db_01")]
    [InlineData(Ruolo.Schema, 4, null, "sch_04")]
    [InlineData(Ruolo.Dominio, 1, null, "dom_01")]
    [InlineData(Ruolo.Login, 7, null, "usr_007")]
    [InlineData(Ruolo.RuoloDb, 1, null, "rol_001")]
    [InlineData(Ruolo.ForeignKey, 5, null, "fk_005")]
    [InlineData(Ruolo.Colonna, 12, "vc50", "col_vc50_012")]
    [InlineData(Ruolo.Colonna, 19, null, "col_x_019")]
    [InlineData(Ruolo.Variabile, 4, "int", "@v_int_004")]
    [InlineData(Ruolo.Parametro, 2, "vc20", "@p_vc20_002")]
    [InlineData(Ruolo.Tabella, 1234, null, "tbl_1234")]
    public void Formato_dei_nomi(Ruolo ruolo, int numero, string? tipo, string atteso) =>
        Assert.Equal(atteso, GeneratoreNomi.Formatta(ruolo, numero, tipo));

    [Theory]
    [InlineData("#Appoggio", "#tmp_003")]
    [InlineData("##Globale", "##tmp_003")]
    public void Le_temporanee_conservano_i_cancelletti(string originale, string atteso) =>
        Assert.Equal(atteso, GeneratoreNomi.Formatta(Ruolo.Temporanea, 3, nomeOriginale: originale));
}
