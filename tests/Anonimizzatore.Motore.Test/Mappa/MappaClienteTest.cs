using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Mappa;

public class MappaClienteTest
{
    [Fact]
    public void Lo_stesso_nome_riceve_sempre_lo_stesso_anonimo()
    {
        var mappa = new MappaCliente();

        var primo = mappa.Ottieni(Ruolo.Tabella, "Ordini");
        var secondo = mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("tbl_001", primo);
        Assert.Equal(primo, secondo);
        Assert.Single(mappa.Voci);
    }

    [Theory]
    [InlineData("ORDINI")]
    [InlineData("[Ordini]")]
    [InlineData("\"ordini\"")]
    [InlineData("  Ordini ")]
    public void Il_confronto_ignora_maiuscole_e_parentesi(string variante)
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("tbl_001", mappa.Ottieni(Ruolo.Tabella, variante));
    }

    [Fact]
    public void Nome_con_parentesi_chiuse_raddoppiate()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "[Strana]]Tabella]");

        Assert.Equal("Strana]Tabella", mappa.Voci.Single().Originale);
    }

    [Fact]
    public void Un_contatore_per_famiglia()
    {
        var mappa = new MappaCliente();

        Assert.Equal("tbl_001", mappa.Ottieni(Ruolo.Tabella, "Ordini"));
        Assert.Equal("tbl_002", mappa.Ottieni(Ruolo.Tabella, "Clienti"));
        Assert.Equal("vw_001", mappa.Ottieni(Ruolo.Vista, "vOrdiniAperti"));
        Assert.Equal("usp_001", mappa.Ottieni(Ruolo.Procedura, "usp_CaricaOrdini"));
        Assert.Equal("db_01", mappa.Ottieni(Ruolo.Database, "Vendite"));
    }

    [Fact]
    public void Lo_stesso_nome_in_famiglie_diverse_ha_nomi_diversi()
    {
        var mappa = new MappaCliente();

        Assert.Equal("db_01", mappa.Ottieni(Ruolo.Database, "Vendite"));
        Assert.Equal("sch_01", mappa.Ottieni(Ruolo.Schema, "Vendite"));
    }

    [Fact]
    public void Login_e_utente_con_lo_stesso_nome_condividono_il_nome_anonimo()
    {
        var mappa = new MappaCliente();

        var login = mappa.Ottieni(Ruolo.Login, "MarioRossi");
        var utente = mappa.Ottieni(Ruolo.Utente, "mariorossi");

        Assert.Equal("usr_001", login);
        Assert.Equal(login, utente);
    }

    [Fact]
    public void Colonne_con_un_unico_contatore_per_tutti_i_tipi()
    {
        var mappa = new MappaCliente();

        Assert.Equal("col_vc50_001", mappa.Ottieni(Ruolo.Colonna, "RagioneSociale", "varchar(50)"));
        Assert.Equal("col_int_002", mappa.Ottieni(Ruolo.Colonna, "IdCliente", "int"));
        Assert.Equal("col_x_003", mappa.Ottieni(Ruolo.Colonna, "Note"));
    }

    [Fact]
    public void Il_tipo_conta_solo_la_prima_volta()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Colonna, "Note");

        Assert.Equal("col_x_001", mappa.Ottieni(Ruolo.Colonna, "Note", "nvarchar(max)"));
    }

    [Fact]
    public void Variabili_e_parametri_condividono_il_contatore()
    {
        var mappa = new MappaCliente();

        Assert.Equal("@v_int_001", mappa.Ottieni(Ruolo.Variabile, "@Contatore", "int"));
        Assert.Equal("@p_vc20_002", mappa.Ottieni(Ruolo.Parametro, "@Codice", "varchar(20)"));
    }

    [Fact]
    public void Temporanee()
    {
        var mappa = new MappaCliente();

        Assert.Equal("#tmp_001", mappa.Ottieni(Ruolo.Temporanea, "#Appoggio"));
        Assert.Equal("##tmp_002", mappa.Ottieni(Ruolo.Temporanea, "##Globale"));
    }

    [Fact]
    public void Trova_originale_ignora_maiuscole_e_parentesi()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("Ordini", mappa.TrovaOriginale("TBL_001")?.Originale);
        Assert.Equal("Ordini", mappa.TrovaOriginale("[tbl_001]")?.Originale);
        Assert.Null(mappa.TrovaOriginale("tbl_999"));
    }

    [Theory]
    [InlineData("col_int_001")]   // l'IA ha sbagliato il tipo
    [InlineData("col_x_001")]
    [InlineData("COL_VC50_001")]
    public void Colonna_trovata_dal_numero_anche_con_tipo_sbagliato(string scrittoDallIa)
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Colonna, "RagioneSociale", "varchar(50)");

        Assert.Equal("RagioneSociale", mappa.TrovaOriginale(scrittoDallIa)?.Originale);
    }

    [Fact]
    public void Variabile_trovata_dal_numero_anche_con_tipo_sbagliato()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Variabile, "@Contatore", "int");

        Assert.Equal("@Contatore", mappa.TrovaOriginale("@v_bint_001")?.Originale);
    }

    [Fact]
    public void Il_nome_generato_non_coincide_con_un_nome_reale()
    {
        // Il cliente ha davvero una tabella che si chiama tbl_001: il generatore la salta.
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "tbl_001");

        Assert.Equal("tbl_002", mappa.Voci.Single().Anonimo);
        Assert.Equal("tbl_003", mappa.Ottieni(Ruolo.Tabella, "Ordini"));
    }

    [Fact]
    public void Il_nome_generato_salta_i_nomi_riservati()
    {
        var mappa = new MappaCliente();
        mappa.AggiungiNomiRiservati(["TBL_001", "[tbl_002]"]);

        Assert.Equal("tbl_003", mappa.Ottieni(Ruolo.Tabella, "Ordini"));
    }

    [Fact]
    public void Rigenera_svuota_e_riparte_da_uno()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Tabella, "Clienti");

        mappa.Rigenera();

        Assert.Empty(mappa.Voci);
        Assert.Equal("tbl_001", mappa.Ottieni(Ruolo.Tabella, "Clienti"));
    }

    [Fact]
    public void Esporta_e_ricarica_mantengono_nomi_e_contatori()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Colonna, "RagioneSociale", "varchar(50)");

        var ricaricata = MappaCliente.Da(mappa.Esporta());

        Assert.Equal("tbl_001", ricaricata.Ottieni(Ruolo.Tabella, "ordini"));
        Assert.Equal("tbl_002", ricaricata.Ottieni(Ruolo.Tabella, "Clienti"));
        Assert.Equal("col_int_002", ricaricata.Ottieni(Ruolo.Colonna, "IdCliente", "int"));
        Assert.Equal("RagioneSociale", ricaricata.TrovaOriginale("col_x_001")?.Originale);
    }

    [Fact]
    public void Nome_vuoto_non_ammesso()
    {
        var mappa = new MappaCliente();

        Assert.Throws<ArgumentException>(() => mappa.Ottieni(Ruolo.Tabella, "[]"));
    }
}
