using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Script di backup con verifica (BACKUP, poi RESTORE VERIFYONLY). Nomi INVENTATI.</summary>
public class BackupVerificaTest
{
    private const string Script = """
        BACKUP DATABASE [VND] TO  DISK = N'F:\Backup\SRV\BAK_10112026\VND.bak' WITH NOFORMAT, NOINIT,  NAME = N'VND-Full Database Backup', SKIP, NOREWIND, NOUNLOAD, COMPRESSION,  STATS = 10
        GO
        declare @backupSetId as int
        select @backupSetId = position from msdb..backupset where database_name=N'VND' and backup_set_id=(select max(backup_set_id) from msdb..backupset where database_name=N'VND' )
        if @backupSetId is null begin raiserror(N'Verify failed. Backup information for database ''VND'' not found.', 16, 1) end
        RESTORE VERIFYONLY FROM  DISK = N'F:\Backup\SRV\BAK_10112026\VND.bak' WITH  FILE = @backupSetId,  NOUNLOAD,  NOREWIND
        GO
        """;

    [Fact]
    public void Lo_script_di_backup_con_verifica_si_anonimizza_senza_errori()
    {
        var esito = AnonimizzatoreScript.Anonimizza([Script], Aiuti.NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.True(esito.Anonimizzato, string.Join("; ", esito.Avvisi.Select(a => a.Messaggio)));
        Assert.Contains("msdb..backupset", esito.Testo!);   // tabella di sistema: resta in chiaro, anche con lo schema vuoto
        Assert.DoesNotContain("[VND]", esito.Testo!);
        Assert.DoesNotContain("VND.bak", esito.Testo!);
    }

    [Fact]
    public void Il_nome_con_schema_vuoto_non_rompe_altri_database()
    {
        var mappa = new MappaCliente();
        var testo = Aiuti.Anon("SELECT * FROM Vendite..Ordini", mappa);

        Assert.DoesNotContain("Vendite", testo);
        Assert.Equal("SELECT * FROM Vendite..Ordini", Aiuti.RipristinaPerProva(testo, mappa));
    }
}
