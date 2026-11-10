using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Controllo;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Anonimizzazione;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Il nome di una variabile dice che cosa contiene: <c>@DBName = 'Vendite'</c> è un database. Nomi INVENTATI.</summary>
public class NomiParlantiTest
{
    [Theory]
    [InlineData("@DBName", Contenuto.Database)]
    [InlineData("@DbName", Contenuto.Database)]
    [InlineData("@DatabaseName", Contenuto.Database)]
    [InlineData("@NomeDB", Contenuto.Database)]
    [InlineData("@SrcDb", Contenuto.Database)]
    [InlineData("@Server", Contenuto.Server)]
    [InlineData("@HostName", Contenuto.Server)]
    [InlineData("@SchemaName", Contenuto.Schema)]
    [InlineData("@UserName", Contenuto.Login)]
    [InlineData("@JobName", Contenuto.Job)]
    [InlineData("@Pwd", Contenuto.Password)]
    [InlineData("@Password", Contenuto.Password)]
    [InlineData("@BackupPath", Contenuto.Nessuno)]
    [InlineData("@DbBackupFile", Contenuto.Nessuno)]
    [InlineData("@Dbo", Contenuto.Nessuno)]
    [InlineData("@backupSetId", Contenuto.Nessuno)]
    [InlineData("@SQL", Contenuto.Nessuno)]
    [InlineData("@DatabaseNote", Contenuto.Nessuno)]
    public void Il_nome_dice_che_cosa_contiene(string nome, Contenuto atteso) =>
        Assert.Equal(atteso, NomiParlanti.Da(nome));

    private const string BackupLog = """
        DECLARE @DBName NVARCHAR(128) = 'VenditeAcme';
        DECLARE @BackupPath NVARCHAR(256) = 'F:\Backup\Dati\BAK_10112026\' + @DBName + '.trn';
        DECLARE @SQL NVARCHAR(MAX);
        SET @SQL = 'BACKUP LOG [' + @DBName + '] TO DISK = ''' + @BackupPath + ''' WITH NAME = ''' + @DBName + '-Log'', SKIP;';
        EXEC sp_executesql @SQL;
        DECLARE @backupSetId INT;
        SELECT @backupSetId = position FROM msdb..backupset WHERE database_name = @DBName
          AND backup_set_id = (SELECT MAX(backup_set_id) FROM msdb..backupset WHERE database_name = @DBName);
        IF @backupSetId IS NULL RAISERROR(N'Verify failed. Backup information not found.', 16, 1);
        PRINT 'Backup del Log verificato con successo per ' + @DBName;
        """;

    [Fact]
    public void Il_database_scritto_solo_nel_valore_di_una_variabile_si_anonimizza()
    {
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo();

        var testo = Aiuti.Anon(BackupLog, mappa, profilo);

        Assert.Contains("DECLARE @DBName NVARCHAR(128) = 'db_01';", testo);
        Assert.DoesNotContain("VenditeAcme", testo);
        Assert.Equal(BackupLog.Replace("\r", ""), Aiuti.RipristinaPerProva(testo, mappa).Replace("\r", ""));
        var esito = ControlloAntiFuga.Verifica([BackupLog], AnonimizzatoreScript.Anonimizza([BackupLog], profilo, new MappaCliente()), mappa, profilo);
        Assert.DoesNotContain(esito.Fughe, f => f.Gravita == GravitaFuga.Bloccante);
    }

    [Fact]
    public void Un_database_di_sistema_nel_valore_resta_in_chiaro()
    {
        var testo = Aiuti.Anon("DECLARE @DBName sysname = 'master', @Db2 sysname = 'Vendite';");

        Assert.Equal("DECLARE @DBName sysname = 'master', @Db2 sysname = 'db_01';", testo);
    }

    [Fact]
    public void Server_schema_utente_e_job_dal_nome_della_variabile()
    {
        var testo = Aiuti.Anon(
            "DECLARE @Server sysname = 'SRVSQL01', @SchemaName sysname = 'vendite', @UserName sysname = 'mario', @JobName sysname = 'BackupNotturno';");

        Assert.Equal(
            "DECLARE @Server sysname = 'srv_01', @SchemaName sysname = 'sch_01', @UserName sysname = 'usr_001', @JobName sysname = 'job_001';",
            testo);
    }

    [Fact]
    public void Una_password_in_una_variabile_si_cancella_e_non_entra_nella_mappa()
    {
        var mappa = new MappaCliente();

        var testo = Aiuti.Anon("DECLARE @Pwd nvarchar(50) = 'Segreta!2026';\nSET @Pwd = 'Altra#1';", mappa);

        Assert.DoesNotContain("Segreta", testo);
        Assert.DoesNotContain("Altra", testo);
        Assert.Contains("'<password>'", testo);
        Assert.DoesNotContain(mappa.Voci, v => v.Originale.Contains("Segreta"));
    }

    [Fact]
    public void SET_parametri_di_EXEC_e_valori_predefiniti_seguono_la_stessa_regola()
    {
        var testo = Aiuti.Anon(
            "CREATE PROCEDURE dbo.usp_Copia @DatabaseOrigine sysname = 'Vendite' AS SELECT 1;\n" +
            "GO\nDECLARE @NomeDb sysname;\nSET @NomeDb = 'Magazzino';\nEXEC dbo.usp_Copia @DatabaseOrigine = 'Vendite';");

        Assert.Contains("@DatabaseOrigine sysname = 'db_01'", testo);
        Assert.Contains("SET @NomeDb = 'db_02';", testo);
        Assert.Contains("@DatabaseOrigine = 'db_01';", testo);
    }

    [Fact]
    public void Colonne_database_name_e_DB_ID_con_una_stringa()
    {
        var testo = Aiuti.Anon(
            "SELECT 1 FROM msdb..backupset WHERE database_name = 'Vendite';\nIF DB_ID('Magazzino') IS NULL PRINT 1;\nSELECT DB_ID('master');");

        Assert.Contains("database_name = 'db_01'", testo);
        Assert.Contains("DB_ID('db_02')", testo);
        Assert.Contains("DB_ID('master')", testo);
    }

    [Fact]
    public void Una_colonna_qualunque_confrontata_con_una_stringa_non_cambia()
    {
        const string sql = "SELECT * FROM dbo.Ordini WHERE Stato = 'Aperto' AND Nome = 'Rossi';";

        Assert.Contains("Stato = 'Aperto' AND Nome = 'Rossi'", Aiuti.Anon(sql));
    }
}
