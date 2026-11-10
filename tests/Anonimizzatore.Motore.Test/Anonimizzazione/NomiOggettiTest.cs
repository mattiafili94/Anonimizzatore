using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Livello 1: nomi di server, database, schema, tabelle, viste, procedure, funzioni e trigger.</summary>
public class NomiOggettiTest
{
    [Fact]
    public void Esempio_vendite_ordini()
    {
        var sql = LeggiEsempio("vendite-ordini.sql");

        var anon = Anon(sql);

        Assert.Contains("USE [db_01];", anon);
        Assert.Contains("CREATE TABLE [sch_01].[tbl_001]", anon);
        Assert.Contains("CREATE PROCEDURE [sch_01].[usp_001]", anon);
        Assert.Contains("FROM   [sch_01].[tbl_001] AS o", anon);
        // Colonne, parametri e alias restano com'erano (in chiaro di default).
        Assert.Contains("IdOrdine     int", anon);
        Assert.Contains("@IdCliente int", anon);
        Assert.Contains("o.IdOrdine", anon);
    }

    [Fact]
    public void Nessun_nome_di_oggetto_resta_in_chiaro()
    {
        var anon = Anon(LeggiEsempio("vendite-ordini.sql"));

        var nomi = Identificatori(anon);

        Assert.DoesNotContain("Vendite", nomi);
        Assert.DoesNotContain("Ordini", nomi);
        Assert.DoesNotContain("usp_OrdiniCliente", nomi);
    }

    [Fact]
    public void Andata_e_ritorno_dell_esempio()
    {
        var sql = LeggiEsempio("vendite-ordini.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        Assert.NotEqual(sql, anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void La_formattazione_non_cambia_solo_i_nomi()
    {
        // Senza commenti l'impaginazione è identica riga per riga.
        var sql = LeggiEsempio("vendite-ordini.sql");
        var senzaCommenti = string.Concat(Motore.Lettura.LettoreScript.Leggi(sql).Token
            .Where(t => t.TokenType is not (Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment
                or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.MultilineComment))
            .Select(t => t.Text));

        var anon = Anon(senzaCommenti);

        Assert.Equal(senzaCommenti.Split('\n').Length, anon.Split('\n').Length);
        Assert.Equal(senzaCommenti.Count(c => c == ' '), anon.Count(c => c == ' '));
    }

    [Fact]
    public void Lo_schema_dbo_resta_in_chiaro()
    {
        Assert.Equal("SELECT * FROM dbo.tbl_001;", Anon("SELECT * FROM dbo.Clienti;"));
    }

    [Theory]
    [InlineData("SELECT name FROM sys.objects;")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES;")]
    [InlineData("SELECT * FROM master.sys.databases;")]
    [InlineData("SELECT * FROM sysobjects;")]
    [InlineData("EXEC sp_executesql @sql;")]
    [InlineData("EXEC master.dbo.xp_cmdshell 'dir';")]
    [InlineData("EXEC msdb.dbo.sp_add_operator @name = 'x';")]
    [InlineData("SELECT * FROM tempdb.sys.tables;")]
    [InlineData("USE master;")]
    [InlineData("SELECT * FROM sys.dm_exec_requests;")]
    public void Oggetti_di_sistema_restano_in_chiaro(string sql) => Assert.Equal(sql, Anon(sql));

    [Fact]
    public void Nomi_con_parentesi_maiuscole_e_diverse_scritture_hanno_lo_stesso_anonimo()
    {
        var anon = Anon("""
            SELECT * FROM [Vendite].[dbo].[Ordini];
            SELECT * FROM vendite.DBO.ORDINI;
            SELECT * FROM "VENDITE"."dbo"."Ordini";
            """);

        Assert.Equal("""
            SELECT * FROM [db_01].[dbo].[tbl_001];
            SELECT * FROM db_01.DBO.tbl_001;
            SELECT * FROM "db_01"."dbo"."tbl_001";
            """, anon);
    }

    [Fact]
    public void Server_collegato_a_quattro_parti()
    {
        var anon = Anon("SELECT * FROM SRVREMOTO.Contabilita.dbo.Fatture;");

        Assert.Equal("SELECT * FROM srv_01.db_01.dbo.tbl_001;", anon);
    }

    [Fact]
    public void Vista_dichiarata_e_poi_usata_diventa_vw_in_entrambi_i_posti()
    {
        var anon = Anon("""
            CREATE VIEW dbo.vOrdiniAperti AS SELECT * FROM dbo.Ordini;
            GO
            SELECT * FROM dbo.vOrdiniAperti;
            """);

        Assert.Contains("CREATE VIEW dbo.vw_001 AS SELECT * FROM dbo.tbl_001;", anon);
        Assert.Contains("SELECT * FROM dbo.vw_001;", anon);
    }

    [Fact]
    public void Vista_usata_prima_della_dichiarazione()
    {
        var anon = Anon("""
            SELECT * FROM dbo.vOrdiniAperti;
            GO
            CREATE VIEW dbo.vOrdiniAperti AS SELECT 1 AS Uno;
            """);

        Assert.Equal(2, Occorrenze(anon, "vw_001"));
    }

    [Fact]
    public void Una_vista_gia_nota_alla_mappa_resta_vista_in_una_nuova_sessione()
    {
        var mappa = new MappaCliente();
        Anon("CREATE VIEW dbo.vOrdiniAperti AS SELECT 1 AS Uno;", mappa);

        var anon = Anon("SELECT * FROM vOrdiniAperti;", mappa);

        Assert.Equal("SELECT * FROM vw_001;", anon);
    }

    [Fact]
    public void Funzioni_procedure_e_trigger()
    {
        var anon = Anon("""
            CREATE FUNCTION dbo.fnSconto(@x int) RETURNS int AS BEGIN RETURN @x; END;
            GO
            CREATE PROCEDURE dbo.uspSomma AS
            BEGIN
                SELECT dbo.fnSconto(5), GETDATE();
                SELECT * FROM dbo.fnMovimenti(1) AS e;
                EXEC dbo.uspAltra;
                EXECUTE [uspSomma];
            END;
            GO
            CREATE TRIGGER dbo.trgOrdini ON dbo.Ordini AFTER INSERT AS
            BEGIN
                SELECT * FROM inserted;
            END;
            """);

        Assert.Contains("CREATE FUNCTION dbo.fn_001(", anon);
        Assert.Contains("CREATE PROCEDURE dbo.usp_001 AS", anon);
        Assert.Contains("SELECT dbo.fn_001(5), GETDATE();", anon);
        Assert.Contains("FROM dbo.fn_002(1) AS e;", anon);
        Assert.Contains("EXEC dbo.usp_002;", anon);
        Assert.Contains("EXECUTE [usp_001];", anon);
        Assert.Contains("CREATE TRIGGER dbo.trg_001 ON dbo.tbl_001 AFTER INSERT", anon);
        Assert.Contains("SELECT * FROM inserted;", anon);
    }

    [Fact]
    public void Insert_update_delete_merge_drop_e_select_into()
    {
        var anon = Anon("""
            INSERT INTO dbo.Ordini (IdOrdine) SELECT IdOrdine FROM dbo.Spedizioni;
            UPDATE o SET Importo = 0 FROM dbo.Ordini AS o;
            DELETE FROM dbo.Spedizioni WHERE IdOrdine = 1;
            SELECT * INTO dbo.Copia FROM dbo.Ordini;
            MERGE dbo.Ordini AS t USING dbo.Spedizioni AS s ON t.IdOrdine = s.IdOrdine WHEN MATCHED THEN DELETE;
            TRUNCATE TABLE dbo.Copia;
            DROP TABLE dbo.Copia;
            DROP VIEW IF EXISTS dbo.vTest;
            """);

        Assert.Contains("INSERT INTO dbo.tbl_001 (IdOrdine) SELECT IdOrdine FROM dbo.tbl_002;", anon);
        Assert.Contains("UPDATE o SET Importo = 0 FROM dbo.tbl_001 AS o;", anon);
        Assert.Contains("DELETE FROM dbo.tbl_002 WHERE", anon);
        Assert.Contains("SELECT * INTO dbo.tbl_003 FROM dbo.tbl_001;", anon);
        Assert.Contains("MERGE dbo.tbl_001 AS t USING dbo.tbl_002 AS s", anon);
        Assert.Contains("TRUNCATE TABLE dbo.tbl_003;", anon);
        Assert.Contains("DROP TABLE dbo.tbl_003;", anon);
        Assert.Contains("DROP VIEW IF EXISTS dbo.vw_001;", anon);
    }

    [Fact]
    public void Foreign_key_e_indici_agganciano_la_tabella_giusta()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Righe (IdRiga int, IdOrdine int,
                CONSTRAINT FK_Righe_Ordini FOREIGN KEY (IdOrdine) REFERENCES dbo.Ordini (IdOrdine));
            CREATE INDEX IX_Righe_Ordine ON dbo.Righe (IdOrdine);
            ALTER TABLE dbo.Righe ADD Note varchar(50);
            """);

        Assert.Contains("CREATE TABLE dbo.tbl_001 (", anon);
        Assert.Contains("REFERENCES dbo.tbl_002 (IdOrdine)", anon);
        Assert.Contains("ON dbo.tbl_001 (IdOrdine);", anon);
        Assert.Contains("ALTER TABLE dbo.tbl_001 ADD", anon);
    }

    // --- prefissi di colonna, alias e nomi locali ---

    [Fact]
    public void Prefisso_di_colonna_con_il_nome_della_tabella()
    {
        var anon = Anon("SELECT Ordini.IdOrdine, Ordini.* FROM Ordini;");

        Assert.Equal("SELECT tbl_001.IdOrdine, tbl_001.* FROM tbl_001;", anon);
    }

    [Fact]
    public void Prefisso_di_colonna_con_schema_e_database()
    {
        var anon = Anon("SELECT Vendite.dbo.Ordini.IdOrdine, dbo.Ordini.Importo FROM Vendite.dbo.Ordini;");

        Assert.Equal("SELECT db_01.dbo.tbl_001.IdOrdine, dbo.tbl_001.Importo FROM db_01.dbo.tbl_001;", anon);
    }

    [Fact]
    public void Alias_e_cte_restano_in_chiaro()
    {
        var anon = Anon("""
            WITH Recenti AS (SELECT o.IdOrdine FROM dbo.Ordini AS o)
            SELECT r.IdOrdine, Recenti.IdOrdine FROM Recenti AS r JOIN dbo.Clienti c ON c.Id = r.IdOrdine;
            """);

        Assert.Equal("""
            WITH Recenti AS (SELECT o.IdOrdine FROM dbo.tbl_001 AS o)
            SELECT r.IdOrdine, Recenti.IdOrdine FROM Recenti AS r JOIN dbo.tbl_002 c ON c.Id = r.IdOrdine;
            """, anon);
    }

    [Fact]
    public void Alias_uguale_al_nome_della_tabella_viene_sostituito_insieme()
    {
        var anon = Anon("SELECT Clienti.Nome FROM dbo.Clienti AS Clienti WHERE Clienti.Id = 1;");

        Assert.Equal("SELECT tbl_001.Nome FROM dbo.tbl_001 AS tbl_001 WHERE tbl_001.Id = 1;", anon);
    }

    [Fact]
    public void Metodi_xml_e_geography_non_sono_funzioni_utente()
    {
        var sql = "SELECT d.x.value('(/a)[1]', 'int'), @geo.STDistance(@g2), d.x.query('/a') FROM @t AS d;";

        Assert.Equal(sql, Anon(sql));
    }

    [Fact]
    public void Tipi_di_dato_non_sono_oggetti()
    {
        var sql = "DECLARE @x varchar(50), @y decimal(18,2); SELECT CAST(@x AS nvarchar(max)), CONVERT(int, @y);";

        Assert.Equal(sql, Anon(sql));
    }

    [Fact]
    public void Tipo_definito_dall_utente_resta_in_chiaro_con_avviso()
    {
        var esito = AnonimizzatoreScript
            .Anonimizza(["DECLARE @c dbo.CodiceCliente;"], NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.Equal("DECLARE @c dbo.CodiceCliente;", esito.Testo);
        Assert.Contains("CodiceCliente", Assert.Single(esito.Avvisi).Messaggio);
    }

    // --- temporanee e profilo ---

    [Fact]
    public void Tabelle_temporanee_in_chiaro_di_default_e_anonimizzate_se_il_profilo_lo_chiede()
    {
        var sql = "SELECT * INTO #Appoggio FROM dbo.Ordini; SELECT #Appoggio.IdOrdine FROM #Appoggio;";

        Assert.Equal("SELECT * INTO #Appoggio FROM dbo.tbl_001; SELECT #Appoggio.IdOrdine FROM #Appoggio;", Anon(sql));

        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Temporanea] = Trattamento.Anonimizza;
        Assert.Equal("SELECT * INTO #tmp_001 FROM dbo.tbl_001; SELECT #tmp_001.IdOrdine FROM #tmp_001;",
            Anon(sql, profilo: profilo));
    }

    [Fact]
    public void La_matrice_del_profilo_decide_per_tipo()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Tabella] = Trattamento.InChiaro;
        profilo.Matrice[Ruolo.Schema] = Trattamento.InChiaro;

        var anon = Anon("SELECT * FROM Vendite.Ordini; EXEC Vendite.uspX;", profilo: profilo);

        Assert.Equal("SELECT * FROM Vendite.Ordini; EXEC Vendite.usp_001;", anon);
    }

    // --- più script, stabilità, collisioni, errori ---

    [Fact]
    public void Piu_script_condividono_la_mappa_e_i_create_dell_uno_servono_all_altro()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["SELECT * FROM dbo.vOrdini;", "CREATE VIEW dbo.vOrdini AS SELECT 1 AS Uno;"],
            NuovoProfilo(), new MappaCliente());

        Assert.Equal("SELECT * FROM dbo.vw_001;", esito.Script[0].Testo);
        Assert.Equal("CREATE VIEW dbo.vw_001 AS SELECT 1 AS Uno;", esito.Script[1].Testo);
    }

    [Fact]
    public void I_nomi_restano_stabili_tra_una_chiamata_e_l_altra()
    {
        var mappa = new MappaCliente();
        Anon("SELECT * FROM dbo.Ordini;", mappa);

        Assert.Equal("SELECT * FROM dbo.tbl_002, dbo.tbl_001;", Anon("SELECT * FROM dbo.Clienti, dbo.Ordini;", mappa));
        Assert.Equal(2, mappa.Voci.Count);
    }

    [Fact]
    public void Un_nome_vero_uguale_a_un_nome_generato_viene_saltato()
    {
        // La colonna si chiama davvero tbl_001 (in chiaro): la tabella non può prendere lo stesso nome.
        var anon = Anon("SELECT tbl_001 FROM dbo.Ordini;");

        Assert.Equal("SELECT tbl_001 FROM dbo.tbl_002;", anon);
    }

    [Fact]
    public void Script_con_errori_non_viene_restituito_e_ha_un_avviso()
    {
        var esito = AnonimizzatoreScript
            .Anonimizza(["SELECT * FORM dbo.Ordini WHERE", "SELECT * FROM dbo.Ordini;"], NuovoProfilo(), new MappaCliente());

        Assert.False(esito.Script[0].Anonimizzato);
        Assert.Null(esito.Script[0].Testo);
        Assert.NotEmpty(esito.Script[0].Avvisi);
        Assert.Equal("SELECT * FROM dbo.tbl_001;", esito.Script[1].Testo); // l'altro script va avanti
    }

    private static int Occorrenze(string testo, string cosa) =>
        (testo.Length - testo.Replace(cosa, "").Length) / cosa.Length;
}
