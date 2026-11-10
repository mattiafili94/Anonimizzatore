using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Colonne, variabili, parametri e alias: in chiaro di default, anonimizzati per eccezione (blacklist).</summary>
public class ColonneVariabiliTest
{
    [Fact]
    public void Senza_blacklist_colonne_variabili_e_alias_restano_in_chiaro()
    {
        var anon = Anon("""
            DECLARE @Codice varchar(20) = 'x', @Totale int;
            SELECT c.RagioneSociale AS Nome, Totale = COUNT(*) FROM dbo.Clienti AS c WHERE c.CodiceFiscale = @Codice;
            """);

        Assert.Equal("""
            DECLARE @Codice varchar(20) = 'x', @Totale int;
            SELECT c.RagioneSociale AS Nome, Totale = COUNT(*) FROM dbo.tbl_001 AS c WHERE c.CodiceFiscale = @Codice;
            """, anon);
    }

    [Fact]
    public void Colonna_in_blacklist_ha_lo_stesso_nome_dappertutto_e_il_tipo_del_create()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Clienti (IdCliente int, CodiceAcme varchar(20), Note nvarchar(max));
            SELECT c.CodiceAcme, Clienti.CodiceAcme FROM dbo.Clienti AS c WHERE CodiceAcme = 'x' ORDER BY CodiceAcme;
            INSERT INTO dbo.Clienti (CodiceAcme) VALUES ('a');
            UPDATE dbo.Clienti SET CodiceAcme = 'b';
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal("""
            CREATE TABLE dbo.tbl_001 (IdCliente int, col_vc20_001 varchar(20), Note nvarchar(max));
            SELECT c.col_vc20_001, tbl_001.col_vc20_001 FROM dbo.tbl_001 AS c WHERE col_vc20_001 = 'x' ORDER BY col_vc20_001;
            INSERT INTO dbo.tbl_001 (col_vc20_001) VALUES ('a');
            UPDATE dbo.tbl_001 SET col_vc20_001 = 'b';
            """, anon);
    }

    [Fact]
    public void Colonna_di_tipo_ignoto_e_colonna_con_tipo_dedotto_da_cast()
    {
        var anon = Anon("""
            SELECT NoteAcme, CAST(DescrizioneAcme AS nvarchar(max)), CONVERT(decimal(18,2), ImportoAcme) FROM dbo.T;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal(
            "SELECT col_x_001, CAST(col_nvcmax_002 AS nvarchar(max)), CONVERT(decimal(18,2), col_dec18_2_003) FROM dbo.tbl_001;",
            anon);
    }

    [Fact]
    public void Alias_di_colonna_si_comporta_come_una_colonna()
    {
        var anon = Anon("SELECT SUM(Importo) AS TotaleAcme FROM dbo.O ORDER BY TotaleAcme;", profilo: NuovoProfilo("Acme"));

        Assert.Equal("SELECT SUM(Importo) AS col_x_001 FROM dbo.tbl_001 ORDER BY col_x_001;", anon);
    }

    [Fact]
    public void Colonne_di_foreign_key_indici_e_primary_key()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Righe (IdRigaAcme int NOT NULL, IdOrdine int,
                CONSTRAINT PK_Righe PRIMARY KEY (IdRigaAcme),
                CONSTRAINT FK_Righe_Ordini FOREIGN KEY (IdOrdine) REFERENCES dbo.Ordini (IdOrdineAcme));
            CREATE INDEX IX_Righe_Riga ON dbo.Righe (IdRigaAcme DESC);
            ALTER TABLE dbo.Righe ALTER COLUMN IdRigaAcme bigint NOT NULL;
            ALTER TABLE dbo.Righe DROP COLUMN IdRigaAcme;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Contains("(col_int_001 int NOT NULL,", anon);
        Assert.Contains("PRIMARY KEY (col_int_001)", anon);
        Assert.Contains("FOREIGN KEY (IdOrdine) REFERENCES dbo.tbl_002 (col_x_002)", anon);
        Assert.Contains("CREATE INDEX IX_tbl_001_Riga ON dbo.tbl_001 (col_int_001 DESC);", anon);
        Assert.Contains("ALTER COLUMN col_int_001 bigint NOT NULL;", anon);
        Assert.Contains("DROP COLUMN col_int_001;", anon);
    }

    [Fact]
    public void Variabili_e_parametri_in_blacklist()
    {
        var anon = Anon("""
            CREATE PROCEDURE dbo.uspX @CodiceAcme varchar(20), @Max int AS
            BEGIN
                DECLARE @ContatoreAcme int = 0, @Altro int;
                SET @ContatoreAcme = @ContatoreAcme + 1;
                SELECT @CodiceAcme, @@ROWCOUNT, @Max, @Altro;
            END;
            GO
            EXEC dbo.uspX @CodiceAcme = 'a', @Max = 3;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal("""
            CREATE PROCEDURE dbo.usp_001 @p_vc20_001 varchar(20), @Max int AS
            BEGIN
                DECLARE @v_int_002 int = 0, @Altro int;
                SET @v_int_002 = @v_int_002 + 1;
                SELECT @p_vc20_001, @@ROWCOUNT, @Max, @Altro;
            END;
            GO
            EXEC dbo.usp_001 @p_vc20_001 = 'a', @Max = 3;
            """, anon);
    }

    [Fact]
    public void I_parametri_delle_procedure_di_sistema_non_si_toccano_nemmeno_con_la_blacklist()
    {
        var sql = "EXEC msdb.dbo.sp_add_job @job_name = N'x', @enabled = 1;";

        Assert.Equal(sql, Anon(sql, profilo: NuovoProfilo("job", "enabled", "name")));
    }

    [Fact]
    public void Alias_di_tabella_e_cte_in_blacklist_cambiano_insieme_alle_loro_citazioni()
    {
        var anon = Anon("""
            WITH AcmeRecenti AS (SELECT o.Id FROM dbo.Ordini AS o)
            SELECT acme.Id, AcmeRecenti.Id FROM dbo.Clienti AS acme JOIN AcmeRecenti ON acme.Id = AcmeRecenti.Id;
            UPDATE acme SET Nome = 'x' FROM dbo.Clienti AS acme;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal("""
            WITH al_001 AS (SELECT o.Id FROM dbo.tbl_001 AS o)
            SELECT al_002.Id, al_001.Id FROM dbo.tbl_002 AS al_002 JOIN al_001 ON al_002.Id = al_001.Id;
            UPDATE al_002 SET Nome = 'x' FROM dbo.tbl_002 AS al_002;
            """, anon);
    }

    [Fact]
    public void La_blacklist_non_tocca_gli_oggetti_di_sistema()
    {
        var sql = "SELECT name FROM sys.objects; USE master; EXEC sp_executesql @s;";

        var anon = Anon(sql, profilo: NuovoProfilo("sys", "objects", "master", "executesql"));

        Assert.Contains("FROM sys.objects;", anon);
        Assert.Contains("USE master;", anon);
        Assert.Contains("EXEC sp_executesql", anon);
    }

    [Fact]
    public void La_matrice_puo_chiedere_di_anonimizzare_tutte_le_colonne_o_i_parametri()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Colonna] = Trattamento.Anonimizza;
        profilo.Matrice[Ruolo.Parametro] = Trattamento.Anonimizza;

        var anon = Anon("""
            CREATE PROCEDURE dbo.uspX @Codice varchar(20) AS SELECT Nome FROM dbo.T WHERE Codice = @Codice;
            """, profilo: profilo);

        Assert.Equal("CREATE PROCEDURE dbo.usp_001 @p_vc20_001 varchar(20) AS SELECT col_x_001 FROM dbo.tbl_001 WHERE col_x_002 = @p_vc20_001;", anon);
    }

    [Fact]
    public void Una_colonna_con_lo_stesso_nome_in_tabelle_diverse_ha_un_solo_nome_anonimo()
    {
        var anon = Anon("SELECT CodiceAcme FROM dbo.A; SELECT CodiceAcme FROM dbo.B;", profilo: NuovoProfilo("Acme"));

        Assert.Equal("SELECT col_x_001 FROM dbo.tbl_001; SELECT col_x_001 FROM dbo.tbl_002;", anon);
    }

    [Fact]
    public void Nomi_tra_parentesi_quadre_e_apici_mantengono_la_forma()
    {
        var anon = Anon("""SELECT [Codice Acme], "ClienteAcme" FROM dbo.T;""", profilo: NuovoProfilo("Acme"));

        Assert.Equal("""SELECT [col_x_001], "col_x_002" FROM dbo.tbl_001;""", anon);
    }

    [Fact]
    public void Esempio_completo_andata_e_ritorno_con_il_vero_DeAnonimizzatore()
    {
        var sql = LeggiEsempio("ordini-righe-acme.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa, NuovoProfilo("Acme"));

        Assert.NotEqual(sql, anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void Esempio_completo_nessuna_parola_della_blacklist_e_nessun_oggetto_in_chiaro()
    {
        var anon = Anon(LeggiEsempio("ordini-righe-acme.sql"), profilo: NuovoProfilo("Acme"));

        // Si guardano gli identificatori (le parole nei commenti sono la tappa 6).
        var nomi = Identificatori(anon);
        Assert.DoesNotContain(nomi, n => n.Contains("acme", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Ordini", nomi);
        Assert.DoesNotContain("Righe", nomi);
        Assert.DoesNotContain("uspTotaleOrdini", nomi);
        Assert.DoesNotContain(nomi, n => n.Contains("Ordini", StringComparison.OrdinalIgnoreCase));
    }
}
