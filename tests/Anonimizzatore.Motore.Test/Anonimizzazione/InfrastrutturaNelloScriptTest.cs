using System.Text.Json;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Mappa;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Livello 3: percorsi, server, IP, email, URL e stringhe di connessione dentro gli script (backup, restore, linked server).</summary>
public class InfrastrutturaNelloScriptTest
{
    // --- backup e restore ---

    [Fact]
    public void Backup_con_percorso_unc_e_nome_del_database()
    {
        var anon = Anon("BACKUP DATABASE Vendite TO DISK = N'\\\\SRVBACKUP\\Condivisa\\Vendite_FULL_20260101.bak' WITH INIT;");

        Assert.Equal("BACKUP DATABASE db_01 TO DISK = N'\\\\srv_01\\dir_001\\db_01_FULL_20260101.bak' WITH INIT;", anon);
    }

    [Fact]
    public void Backup_del_log_percorso_locale_con_cartelle_di_sistema_in_chiaro()
    {
        var anon = Anon("BACKUP LOG [Vendite] TO DISK = 'D:\\Backup\\Vendite_LOG.trn';");

        Assert.Equal("BACKUP LOG [db_01] TO DISK = 'D:\\Backup\\db_01_LOG.trn';", anon);
    }

    [Fact]
    public void Restore_con_move_nomi_logici_e_percorsi()
    {
        var anon = Anon("""
            RESTORE DATABASE Vendite_Copia FROM DISK = N'E:\Backup\Vendite.bak'
                WITH MOVE N'Vendite_Data' TO N'D:\Dati\Vendite_Copia.mdf', MOVE N'Vendite_Log' TO N'E:\Log\Vendite_Copia.ldf', REPLACE;
            """);

        Assert.Equal("""
            RESTORE DATABASE db_01 FROM DISK = N'E:\Backup\file_003.bak'
                WITH MOVE N'file_001' TO N'D:\Dati\db_01.mdf', MOVE N'file_002' TO N'E:\Log\db_01.ldf', REPLACE;
            """, anon);
    }

    [Fact]
    public void Create_database_con_nomi_logici_e_percorsi_dei_file()
    {
        var anon = Anon("""
            CREATE DATABASE Vendite ON PRIMARY (NAME = Vendite_Data, FILENAME = 'D:\Dati\Vendite.mdf')
                LOG ON (NAME = Vendite_Log, FILENAME = 'E:\Log\Vendite.ldf');
            """);

        Assert.Equal("""
            CREATE DATABASE db_01 ON PRIMARY (NAME = file_001, FILENAME = 'D:\Dati\db_01.mdf')
                LOG ON (NAME = file_002, FILENAME = 'E:\Log\db_01.ldf');
            """, anon);
    }

    [Fact]
    public void Alter_drop_e_dbcc_sul_database()
    {
        var anon = Anon("""
            ALTER DATABASE Vendite SET RECOVERY SIMPLE;
            ALTER DATABASE Vendite MODIFY NAME = Vendite_Vecchio;
            DBCC CHECKDB (N'Vendite_Vecchio') WITH NO_INFOMSGS;
            DBCC CHECKDB (Vendite_Vecchio);
            DBCC SHRINKFILE (N'Vendite_Log', 1);
            DBCC CHECKDB (N'master');
            DROP DATABASE IF EXISTS Vendite_Vecchio;
            """);

        Assert.Equal("""
            ALTER DATABASE db_01 SET RECOVERY SIMPLE;
            ALTER DATABASE db_01 MODIFY NAME = db_02;
            DBCC CHECKDB (N'db_02') WITH NO_INFOMSGS;
            DBCC CHECKDB (db_02);
            DBCC SHRINKFILE (N'file_001', 1);
            DBCC CHECKDB (N'master');
            DROP DATABASE IF EXISTS db_02;
            """, anon);
    }

    [Fact]
    public void Dispositivo_di_backup_logico()
    {
        Assert.Equal("BACKUP DATABASE db_01 TO file_001;", Anon("BACKUP DATABASE Vendite TO DispositivoVendite;"));
    }

    // --- server collegati e stringhe di connessione ---

    [Fact]
    public void Lo_stesso_server_ha_lo_stesso_nome_in_datasrc_e_nella_stringa_di_connessione()
    {
        var anon = Anon("""
            EXEC sp_addlinkedserver @server = N'SRVREMOTO', @srvproduct = N'', @provider = N'SQLNCLI', @datasrc = N'SRVSQL02\ISTANZA1', @catalog = N'Contabilita';
            SELECT * FROM OPENROWSET('SQLNCLI', 'Server=SRVSQL02\ISTANZA1;Database=Contabilita;Trusted_Connection=yes;', 'SELECT 1');
            """);

        Assert.Equal("""
            EXEC sp_addlinkedserver @server = N'srv_01', @srvproduct = N'', @provider = N'SQLNCLI', @datasrc = N'srv_02\srv_03', @catalog = N'db_01';
            SELECT * FROM OPENROWSET('SQLNCLI', 'Server=srv_02\srv_03;Database=db_01;Trusted_Connection=yes;', 'SELECT 1');
            """, anon);
    }

    [Fact]
    public void Password_e_user_nelle_stringhe_di_connessione()
    {
        var mappa = new MappaCliente();
        var esito = AnonimizzatoreScript.Anonimizza(
            ["DECLARE @c nvarchar(300) = N'Data Source=SRVSQL02;Initial Catalog=Vendite;User ID=ACMEDOM\\svc_app;Password=Pwd123!;';"],
            NuovoProfilo(), mappa).Script[0];

        Assert.Equal(
            "DECLARE @c nvarchar(300) = N'Data Source=srv_01;Initial Catalog=db_01;User ID=dom_01\\usr_001;Password=<password>;';",
            esito.Testo);
        Assert.Contains(esito.Avvisi, a => a.Messaggio.Contains("password") && a.Riga == 1);
        Assert.DoesNotContain("Pwd123", JsonSerializer.Serialize(mappa.Esporta()));
    }

    [Fact]
    public void Connessione_con_sa_resta_sa_e_la_password_sparisce()
    {
        var anon = Anon("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=SRVSQL02;UID=sa;PWD=Segreta!;', 'SELECT 1');");

        Assert.Equal("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=srv_01;UID=sa;PWD=<password>;', 'SELECT 1');", anon);
    }

    // --- IP, email, URL, comandi di sistema ---

    [Fact]
    public void Email_ip_e_url_nelle_stringhe()
    {
        var anon = Anon("""
            DECLARE @a varchar(200) = 'dba@acme.example', @b varchar(50) = '10.0.0.5', @c varchar(200) = 'https://SRVSQL02.acme.example/api';
            EXEC msdb.dbo.sp_send_dbmail @recipients = 'dba@acme.example', @subject = 'Prova';
            """);

        Assert.Equal("""
            DECLARE @a varchar(200) = 'mail_001@example.invalid', @b varchar(50) = 'ip_001', @c varchar(200) = 'https://srv_01.dom_01.dom_02/api';
            EXEC msdb.dbo.sp_send_dbmail @recipients = 'mail_001@example.invalid', @subject = 'Prova';
            """, anon);
    }

    [Fact]
    public void Comando_di_sistema_con_percorsi_e_unita_di_rete()
    {
        var anon = Anon("EXEC xp_cmdshell 'copy D:\\Backup\\Vendite_LOG.trn \\\\10.0.0.5\\archivio$\\';");

        // "Vendite" non è un database noto qui: il file prende un nome di file (file_001), non db_NN.
        Assert.Equal("EXEC xp_cmdshell 'copy D:\\Backup\\file_001.trn \\\\ip_001\\dir_001\\';", anon);
    }

    [Fact]
    public void Gli_apici_e_il_prefisso_N_si_conservano()
    {
        var anon = Anon("SELECT N'D:\\Dati\\L''Aquila\\Vendite.bak', 'D:\\Dati\\L''Aquila\\Vendite.bak';");

        Assert.Equal("SELECT N'D:\\Dati\\dir_001\\file_001.bak', 'D:\\Dati\\dir_001\\file_001.bak';", anon);
    }

    // --- cose che NON devono cambiare ---

    [Theory]
    [InlineData("SELECT 'SELECT 1', 'Prova', 'x', N'Report mensile', '2026-01-31', '15.0.4000.1';")]
    [InlineData("PRINT 'Elaborazione completata alle 12:30';")]
    [InlineData("SELECT 'dbo', 'sys.objects', 'a.b.c';")]
    [InlineData("SET @v = 'versione 2.1.3';")]
    public void Stringhe_senza_infrastruttura_restano_uguali(string sql) => Assert.Equal(sql, Anon(sql.Replace("SET @v", "DECLARE @v varchar(20); SET @v")).Replace("DECLARE @v varchar(20); SET @v", "SET @v"));

    [Fact]
    public void I_nomi_dentro_le_stringhe_gia_trattati_non_vengono_trattati_due_volte()
    {
        // 'ACMEDOM\mario' è già un nome (EXECUTE AS): diventa dom_01\usr_001 una volta sola.
        Assert.Equal("EXECUTE AS LOGIN = N'dom_01\\usr_001';", Anon("EXECUTE AS LOGIN = N'ACMEDOM\\mario';"));
    }

    // --- esempio completo ---

    [Fact]
    public void Esempio_completo_nessun_dato_del_cliente_resta_tranne_i_testi_liberi()
    {
        var anon = Anon(LeggiEsempio("backup-restore-acme.sql"), profilo: NuovoProfilo("Acme"));

        foreach (var vietata in new[] { "SRVBACKUP", "SRVSQL02", "SRVREMOTO", "ISTANZA1", "Condivisa", "Contabilita", "ACMEDOM",
                     "svc_app", "utente_remoto", "Segr3ta", "Pwd123", "acme", "10.0.0.5", "archivio", "Vendite_Data", "Vendite_Copia" })
            Assert.DoesNotContain(vietata, anon, StringComparison.OrdinalIgnoreCase);

        // I due testi liberi (NAME del backup, percorso dell'URL) hanno i nomi già anonimizzati al posto di quelli veri.
        Assert.Contains("NAME = N'db_01 full'", anon);
        Assert.Contains("/report/db_01?anno=2026", anon);
    }

    [Fact]
    public void Esempio_completo_andata_e_ritorno_con_il_vero_DeAnonimizzatore()
    {
        var sql = LeggiEsempio("backup-restore-acme.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        // Le password sono cancellate e non tornano: nel risultato c'è il segnaposto.
        var atteso = sql
            .Replace("'Segr3ta'", "'<password>'")
            .Replace("Password=Pwd123!;", "Password=<password>;");
        // Dentro un testo libero un nome torna con le maiuscole della prima volta che è stato scritto (limite noto della mappa:
        // "/report/vendite" torna "/report/Vendite"), quindi si confronta senza distinguere maiuscole e minuscole.
        Assert.Equal(atteso, RipristinaPerProva(anon, mappa), ignoreCase: true);
    }

    [Fact]
    public void Nella_risposta_dell_IA_i_nomi_nei_percorsi_e_nel_codice_tornano_nel_modo_giusto()
    {
        var mappa = new MappaCliente();
        Anon("""
            BACKUP DATABASE [Vendite-Nuove] TO DISK = N'\\SRV-BACKUP\Condivisa\Vendite-Nuove.bak';
            SELECT * FROM [SRV-REMOTO].[Vendite-Nuove].dbo.Ordini;
            """, mappa);

        // Risposta dell'IA: gli stessi nomi anonimi, una volta in un percorso e una volta nel codice SQL.
        var risposta = """
            Il file è in \\srv_02\dir_001\db_01.bak. Puoi leggerlo con:
            SELECT * FROM srv_01.db_01.dbo.tbl_001;
            """;

        var esito = new DeAnonimizzatore(mappa).DeAnonimizza(risposta);

        // Nel percorso niente parentesi quadre; nel codice, sì (i nomi hanno un trattino).
        Assert.Contains(@"\\SRV-BACKUP\Condivisa\Vendite-Nuove.bak", esito.Testo);
        Assert.Contains("SELECT * FROM [SRV-REMOTO].[Vendite-Nuove].dbo.Ordini;", esito.Testo);
    }

    [Fact]
    public void Rianonimizzare_lo_stesso_script_da_gli_stessi_nomi()
    {
        var mappa = new MappaCliente();
        var sql = LeggiEsempio("backup-restore-acme.sql");

        var primo = Anon(sql, mappa);
        var secondo = Anon(sql, mappa);

        Assert.Equal(primo, secondo);
    }
}
