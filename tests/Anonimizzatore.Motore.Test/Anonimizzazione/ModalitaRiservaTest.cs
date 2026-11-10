using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Script con errori di sintassi: modalità di riserva (solo token), sempre con un avviso.</summary>
public class ModalitaRiservaTest
{
    private static ScriptAnonimizzato Riserva(string sql, MappaCliente? mappa = null, params string[] blacklist)
    {
        var esito = AnonimizzatoreScript.Anonimizza([sql], NuovoProfilo(blacklist), mappa ?? new MappaCliente()).Script[0];
        Assert.True(esito.Anonimizzato, string.Join("; ", esito.Avvisi.Select(a => a.Messaggio)));
        Assert.Contains(esito.Avvisi, a => a.Messaggio.Contains("modalità di riserva"));
        return esito;
    }

    [Fact]
    public void Il_contesto_rivela_le_tabelle_dopo_from_join_into_update()
    {
        var esito = Riserva("""
            SELECT o.Id FROM dbo.Ordini o JOIN dbo.Righe r ON (o.Id = r.IdOrdine
            INSERT INTO Storici SELECT 1 WHERE (
            UPDATE dbo.Clienti SET x = (
            """);

        Assert.Equal("""
            SELECT o.Id FROM dbo.tbl_001 o JOIN dbo.tbl_002 r ON (o.Id = r.IdOrdine
            INSERT INTO tbl_003 SELECT 1 WHERE (
            UPDATE dbo.tbl_004 SET x = (
            """, esito.Testo);
    }

    [Fact]
    public void Procedura_senza_end_e_nomi_a_piu_parti()
    {
        var esito = Riserva("""
            CREATE PROCEDURE Vendite.dbo.uspChiudi @a int AS
            BEGIN
                SELECT * FROM SRVREMOTO.Contabilita.dbo.Fatture;
                EXEC dbo.uspAltra;
            """);

        Assert.Equal("""
            CREATE PROCEDURE db_01.dbo.usp_001 @a int AS
            BEGIN
                SELECT * FROM srv_01.db_02.dbo.tbl_001;
                EXEC dbo.usp_002;
            """, esito.Testo);
    }

    [Fact]
    public void Use_database_schema_view_function_trigger()
    {
        var esito = Riserva("""
            USE Vendite;
            CREATE SCHEMA Acquisti;
            CREATE VIEW Acquisti.vOrdini AS SELECT 1 FROM
            CREATE FUNCTION dbo.fnSconto () RETURNS
            CREATE TRIGGER dbo.trgOrdini ON
            BACKUP DATABASE Vendite TO DISK = N'\\SRVBACKUP\Condivisa\Vendite.bak' WITH
            """);

        Assert.Equal("""
            USE db_01;
            CREATE SCHEMA sch_01;
            CREATE VIEW sch_01.vw_001 AS SELECT 1 FROM
            CREATE FUNCTION dbo.fn_001 () RETURNS
            CREATE TRIGGER dbo.trg_001 ON
            BACKUP DATABASE db_01 TO DISK = N'\\srv_01\dir_001\db_01.bak' WITH
            """, esito.Testo);
    }

    [Fact]
    public void Gli_oggetti_di_sistema_restano_in_chiaro()
    {
        var sql = "USE master;\nSELECT * FROM sys.objects o JOIN INFORMATION_SCHEMA.TABLES t ON (\nEXEC sp_who2 @x = ";

        Assert.Equal(sql, Riserva(sql).Testo);
    }

    [Fact]
    public void I_nomi_gia_noti_dagli_altri_script_si_riconoscono_ovunque()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["CREATE TABLE dbo.Righe (Id int, IdOrdine int);\nCREATE TABLE dbo.Ordini (Id int);",
             "SELECT Righe.Id, Ordini.Id WHERE Righe.IdOrdine = Ordini.Id AND ("],
            NuovoProfilo(), new MappaCliente());

        Assert.Equal("SELECT tbl_001.Id, tbl_002.Id WHERE tbl_001.IdOrdine = tbl_002.Id AND (", esito.Script[1].Testo);
    }

    [Fact]
    public void I_nomi_gia_noti_da_una_sessione_precedente_si_riconoscono_anche_con_errori()
    {
        var mappa = new MappaCliente();
        Anon("SELECT * FROM dbo.Ordini;", mappa);

        var esito = Riserva("SELECT Ordini.Id WHERE (", mappa);

        Assert.Equal("SELECT tbl_001.Id WHERE (", esito.Testo);
    }

    [Fact]
    public void Blacklist_colonne_e_variabili()
    {
        var esito = Riserva("SELECT CodiceAcme, Nome, @ClienteAcme FROM dbo.Ordini WHERE (", null, "Acme");

        Assert.Equal("SELECT col_x_001, Nome, @v_x_001 FROM dbo.tbl_001 WHERE (", esito.Testo);
    }

    [Fact]
    public void Password_e_sid_si_cancellano_anche_senza_albero()
    {
        var esito = Riserva("CREATE LOGIN svc WITH PASSWORD = N'Segr3ta', SID = 0x1A2B3C4D5E6F708192A3B4C5D6E7F801 ??? ");

        Assert.DoesNotContain("Segr3ta", esito.Testo);
        Assert.DoesNotContain("1A2B3C", esito.Testo);
        Assert.Contains("PASSWORD = N'<password>'", esito.Testo);
        Assert.Contains("SID = 0x00000000000000000000000000000000", esito.Testo);
    }

    [Fact]
    public void Commenti_e_stringhe_si_trattano_come_negli_script_validi()
    {
        var esito = Riserva("""
            -- password del cliente: Segreta
            SELECT * FROM dbo.Ordini WHERE x = (
            EXEC xp_cmdshell 'copy D:\Dati\Acme\a.bak \\10.0.0.5\archivio$\';
            """);

        Assert.DoesNotContain("Segreta", esito.Testo);
        Assert.DoesNotContain("10.0.0.5", esito.Testo);
        Assert.Contains("/* c001 */", esito.Testo);
    }

    [Fact]
    public void L_avviso_porta_il_messaggio_e_la_riga_dell_errore()
    {
        var esito = Riserva("SELECT 1;\nSELECT * FROM dbo.Ordini WHERE (\n");

        var avviso = Assert.Single(esito.Avvisi);
        Assert.Equal(2, avviso.Riga);
        Assert.Contains("Errore di sintassi", avviso.Messaggio);
    }

    [Fact]
    public void Andata_e_ritorno_in_modalita_di_riserva()
    {
        var sql = """
            USE Vendite;
            CREATE PROCEDURE Vendite.dbo.uspChiudi @CodiceAcme varchar(20) AS
            BEGIN
                -- chiude gli ordini
                UPDATE dbo.Ordini SET Stato = 1 WHERE CodiceAcme = @CodiceAcme AND (
            """;
        var mappa = new MappaCliente();

        var anon = Riserva(sql, mappa, "Acme").Testo!;

        Assert.DoesNotContain("Vendite", anon);
        Assert.DoesNotContain("Ordini", anon);
        Assert.DoesNotContain("Acme", anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }
}
