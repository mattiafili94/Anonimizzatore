using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Whitelist (nome intero e parole) e blacklist: la blacklist vince sempre.</summary>
public class WhitelistBlacklistTest
{
    private static Profilo ProfiloCon(string[]? whitelist = null, string[]? blacklist = null) =>
        new() { Nome = "Prova", Whitelist = [.. whitelist ?? []], Blacklist = [.. blacklist ?? []] };

    [Fact]
    public void Oggetti_con_nomi_generici_restano_in_chiaro_con_la_lista_base()
    {
        var anon = Anon("""
            SELECT * FROM dbo.Log;
            EXEC dbo.usp_GetData;
            SELECT * FROM Report.StoricoErrori;
            SELECT * FROM dbo.Ordini;
            EXEC dbo.usp_GetOrdini;
            """);

        Assert.Equal("""
            SELECT * FROM dbo.Log;
            EXEC dbo.usp_GetData;
            SELECT * FROM Report.StoricoErrori;
            SELECT * FROM dbo.tbl_001;
            EXEC dbo.usp_001;
            """, anon);
    }

    [Fact]
    public void Voce_del_cliente_per_parola_tiene_in_chiaro_i_nomi_che_la_contengono_da_sola()
    {
        var anon = Anon("SELECT * FROM dbo.Ordini; EXEC dbo.usp_GetOrdini; SELECT * FROM dbo.OrdiniAcme;",
            profilo: ProfiloCon(whitelist: ["Ordini"]));

        Assert.Equal("SELECT * FROM dbo.Ordini; EXEC dbo.usp_GetOrdini; SELECT * FROM dbo.tbl_001;", anon);
    }

    [Fact]
    public void Voce_del_cliente_per_nome_intero_non_rende_generiche_le_sue_parole()
    {
        var anon = Anon("SELECT * FROM dbo.OrdiniAperti; SELECT * FROM dbo.Ordini;",
            profilo: ProfiloCon(whitelist: ["OrdiniAperti"]));

        Assert.Equal("SELECT * FROM dbo.OrdiniAperti; SELECT * FROM dbo.tbl_001;", anon);
    }

    [Fact]
    public void La_blacklist_vince_sulla_whitelist()
    {
        var anon = Anon("SELECT * FROM dbo.Acme; SELECT * FROM dbo.AcmeData; SELECT * FROM dbo.Data;",
            profilo: ProfiloCon(whitelist: ["Acme"], blacklist: ["Acme"]));

        Assert.Equal("SELECT * FROM dbo.tbl_001; SELECT * FROM dbo.tbl_002; SELECT * FROM dbo.Data;", anon);
    }

    [Fact]
    public void La_whitelist_vale_anche_per_gli_altri_tipi_di_oggetto()
    {
        var anon = Anon("""
            USE Report;
            SELECT * FROM Report.dbo.Storico;
            EXEC msdb.dbo.sp_add_operator @name = N'x';
            """);

        Assert.Equal("""
            USE Report;
            SELECT * FROM Report.dbo.Storico;
            EXEC msdb.dbo.sp_add_operator @name = N'x';
            """, anon);
    }

    [Fact]
    public void Un_nome_gia_anonimizzato_resta_stabile_anche_se_poi_entra_in_whitelist()
    {
        var mappa = new MappaCliente();
        Anon("SELECT * FROM dbo.Ordini;", mappa);                                  // tbl_001

        var anon = Anon("SELECT * FROM dbo.Ordini;", mappa, ProfiloCon(whitelist: ["Ordini"]));

        Assert.Equal("SELECT * FROM dbo.tbl_001;", anon);                          // resta così fino a "rigenera nomi"

        mappa.Rigenera();
        Assert.Equal("SELECT * FROM dbo.Ordini;", Anon("SELECT * FROM dbo.Ordini;", mappa, ProfiloCon(whitelist: ["Ordini"])));
    }

    [Fact]
    public void La_whitelist_non_rende_anonimo_cio_che_la_matrice_lascia_in_chiaro()
    {
        var profilo = ProfiloCon();
        profilo.Matrice[Ruolo.Tabella] = Trattamento.InChiaro;

        Assert.Equal("SELECT * FROM dbo.Ordini;", Anon("SELECT * FROM dbo.Ordini;", profilo: profilo));
    }

    [Fact]
    public void Con_la_matrice_che_anonimizza_le_colonne_quelle_generiche_restano_in_chiaro()
    {
        var profilo = ProfiloCon();
        profilo.Matrice[Ruolo.Colonna] = Trattamento.Anonimizza;

        var anon = Anon("SELECT Id, Nome, Matricola, CodiceFiscale FROM dbo.Ordini;", profilo: profilo);

        Assert.Equal("SELECT Id, Nome, col_x_001, col_x_002 FROM dbo.tbl_001;", anon);
    }

    [Fact]
    public void Nomi_composti_di_indici_e_constraint_generici_restano_in_chiaro_anche_se_la_matrice_li_anonimizza()
    {
        var profilo = ProfiloCon(blacklist: ["Acme"]);
        profilo.Matrice[Ruolo.PrimaryKey] = Trattamento.Anonimizza;
        profilo.Matrice[Ruolo.Indice] = Trattamento.Anonimizza;

        var anon = Anon("""
            CREATE TABLE dbo.Ordini (Id int CONSTRAINT PK_Ordini PRIMARY KEY, Y int CONSTRAINT PK_Data PRIMARY KEY);
            CREATE INDEX IX_Data ON dbo.Ordini (Id);
            CREATE INDEX IX_Acme_Data ON dbo.Ordini (Id);
            """, profilo: profilo);

        Assert.Contains("CONSTRAINT pk_001 PRIMARY KEY, Y int CONSTRAINT PK_Data PRIMARY KEY", anon);
        Assert.Contains("CREATE INDEX IX_Data ON", anon);
        Assert.Contains("CREATE INDEX ix_001 ON", anon);
    }

    [Fact]
    public void Andata_e_ritorno_con_whitelist_e_blacklist()
    {
        var sql = """
            CREATE TABLE dbo.Ordini (Id int, CodiceAcme varchar(20));
            CREATE TABLE dbo.Log (Id int, Messaggio nvarchar(max));
            CREATE INDEX IX_Ordini_Data ON dbo.Ordini (Id);
            EXEC dbo.usp_GetData;
            SELECT o.CodiceAcme FROM dbo.Ordini AS o JOIN dbo.Log AS l ON l.Id = o.Id;
            """;
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa, ProfiloCon(blacklist: ["Acme"]));

        Assert.Contains("dbo.Log", anon);
        Assert.Contains("usp_GetData", anon);
        Assert.DoesNotContain("Ordini", anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }
}
