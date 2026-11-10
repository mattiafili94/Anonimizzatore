using Anonimizzatore.Motore.Infrastruttura;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Infrastruttura;

// Tutti i nomi (server, cartelle, persone, domini) sono INVENTATI.
public class AnalizzatoreInfrastrutturaTest
{
    private static (MappaCliente Mappa, AnalizzatoreInfrastruttura Analizzatore) Nuovo(bool testoLibero = false)
    {
        var mappa = new MappaCliente();
        return (mappa, new AnalizzatoreInfrastruttura(mappa, new OpzioniInfrastruttura(testoLibero)));
    }

    private static void Invariato(string testo, bool testoLibero = false)
    {
        var (_, a) = Nuovo(testoLibero);
        var r = a.Anonimizza(testo);
        Assert.Equal(testo, r.Testo);
        Assert.Empty(r.Sostituzioni);
    }

    // ───────────── Percorsi ─────────────

    [Fact]
    public void Percorso_unc_con_database_noto()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");

        var r = a.Anonimizza(@"\\SRVBACKUP\Condivisa\Vendite.bak");

        Assert.Equal(@"\\srv_01\dir_001\db_01.bak", r.Testo);
        var s = Assert.Single(r.Sostituzioni);
        Assert.Equal(TipoInfrastruttura.PercorsoUnc, s.Tipo);
        Assert.Equal(0, s.Posizione);
    }

    [Fact]
    public void Percorso_unc_senza_database_noto_usa_file()
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"\\srv_01\dir_001\file_001.bak", a.Anonimizza(@"\\SRVBACKUP\Condivisa\Vendite.bak").Testo);
    }

    [Fact]
    public void Percorso_locale_mantiene_cartelle_generiche_ed_estensione()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");

        var r = a.Anonimizza(@"D:\Dati\Vendite_Dati.mdf");

        Assert.Equal(@"D:\Dati\db_01_Dati.mdf", r.Testo);
        Assert.Equal(TipoInfrastruttura.PercorsoLocale, r.Sostituzioni.Single().Tipo);
    }

    [Fact]
    public void Cartelle_di_sistema_di_SQL_Server_restano_in_chiaro()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");

        var r = a.Anonimizza(@"C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\Vendite_Dati.mdf");

        Assert.Equal(@"C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\db_01_Dati.mdf", r.Testo);
    }

    [Fact]
    public void Istanza_con_nome_nella_cartella_MSSQL_viene_anonimizzata()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"C:\Program Files\Microsoft SQL Server\MSSQL15.GESTIONALE\MSSQL\Backup\x.bak");
        Assert.Equal(@"C:\Program Files\Microsoft SQL Server\MSSQL15.srv_01\MSSQL\Backup\file_001.bak", r.Testo);
    }

    [Fact]
    public void Percorso_con_spazi()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"D:\Dati Clienti\Vendite 2026.bak");
        Assert.Equal(@"D:\dir_001\file_001.bak", r.Testo);
    }

    [Fact]
    public void Percorso_con_spazi_non_inghiotte_un_secondo_percorso()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"copy D:\Archivio Rossi\x.bak E:\Copie\x.bak");
        Assert.Equal(@"copy D:\dir_001\file_001.bak E:\dir_002\file_001.bak", r.Testo);
        Assert.Equal(2, r.Sostituzioni.Count);
    }

    [Fact]
    public void Percorso_con_spazi_si_chiude_alla_estensione_nota()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"D:\Archivio Rossi\x.bak WITH NOINIT");
        Assert.Equal(@"D:\dir_001\file_001.bak WITH NOINIT", r.Testo);
    }

    [Fact]
    public void Percorso_chiuso_dall_apice_della_stringa()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");
        var r = a.Anonimizza(@"BACKUP DATABASE X TO DISK = N'D:\Backup\Vendite.bak' WITH INIT");
        Assert.Equal(@"BACKUP DATABASE X TO DISK = N'D:\Backup\db_01.bak' WITH INIT", r.Testo);
    }

    [Fact]
    public void Percorso_con_slash()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");

        Assert.Equal("D:/Dati/db_01.bak", a.Anonimizza("D:/Dati/Vendite.bak").Testo);
        Assert.Equal("/var/opt/mssql/data/db_01.mdf", a.Anonimizza("/var/opt/mssql/data/Vendite.mdf").Testo);
        Assert.Equal("//srv_01/dir_001/db_01.bak", a.Anonimizza("//SRVFILE/Condivisa/Vendite.bak").Testo);
    }

    [Fact]
    public void Percorso_con_slash_misti_conserva_i_separatori()
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"D:\dir_001/file_001.bak", a.Anonimizza(@"D:\Rossi/x.bak").Testo);
    }

    [Theory]
    [InlineData(".bak")]
    [InlineData(".mdf")]
    [InlineData(".ndf")]
    [InlineData(".ldf")]
    [InlineData(".trn")]
    [InlineData(".sql")]
    [InlineData(".csv")]
    [InlineData(".dtsx")]
    public void Le_estensioni_restano(string estensione)
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"D:\Dati\file_001" + estensione, a.Anonimizza(@"D:\Dati\Rossi" + estensione).Testo);
    }

    [Fact]
    public void Cartella_utente_e_condivisione_amministrativa()
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"C:\Users\dir_001\Desktop\export.csv", a.Anonimizza(@"C:\Users\mario.rossi\Desktop\export.csv").Testo);
        Assert.Equal(@"C:\Users\Public\file_001.csv", a.Anonimizza(@"C:\Users\Public\x.csv").Testo);
        Assert.Equal(@"\\srv_01\D$\Backup\file_001.bak", a.Anonimizza(@"\\SRV\D$\Backup\x.bak").Testo);
    }

    [Fact]
    public void Ip_dentro_a_un_percorso_unc()
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"\\ip_001\dir_001\file_001.bak", a.Anonimizza(@"\\10.1.2.3\Condivisa\a.bak").Testo);
    }

    [Fact]
    public void Variabili_d_ambiente_e_token_di_agent_restano()
    {
        var (_, a) = Nuovo();
        Assert.Equal(@"C:\Dati\%TEMP%\file_001.txt", a.Anonimizza(@"C:\Dati\%TEMP%\y.txt").Testo);
        Assert.Equal(@"C:\Dati\$(ESCAPE_SQUOTE(JOBID))\file_002.log", a.Anonimizza(@"C:\Dati\$(ESCAPE_SQUOTE(JOBID))\z.log").Testo);
    }

    [Fact]
    public void Percorso_in_testo_libero_si_chiude_alla_punteggiatura()
    {
        var (mappa, a) = Nuovo(testoLibero: true);
        mappa.Ottieni(Ruolo.Database, "Vendite");

        var r = a.Anonimizza(@"Il backup è in D:\Backup\Vendite.bak, poi copia su \\SRV2\Condivisa\Vendite.bak.");

        Assert.Equal(@"Il backup è in D:\Backup\db_01.bak, poi copia su \\srv_01\dir_001\db_01.bak.", r.Testo);
    }

    [Fact]
    public void Testo_libero_conosce_Program_Files_x86()
    {
        var (_, a) = Nuovo(testoLibero: true);
        var r = a.Anonimizza(@"Installato in C:\Program Files (x86)\Gestionale\app.dll per ora");
        Assert.Equal(@"Installato in C:\Program Files (x86)\dir_001\file_001.dll per ora", r.Testo);
    }

    [Fact]
    public void Testo_libero_gli_spazi_chiudono_il_percorso()
    {
        var (_, a) = Nuovo(testoLibero: true);
        var r = a.Anonimizza(@"vai in D:\Rossi dentro la cartella");
        Assert.Equal(@"vai in D:\dir_001 dentro la cartella", r.Testo);
    }

    // ───────────── IP ─────────────

    [Fact]
    public void Ip_validi_diventano_ip_NNN_e_restano_stabili()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("collegati a 10.20.30.40 oppure 10.20.30.41, poi di nuovo 10.20.30.40");
        Assert.Equal("collegati a ip_001 oppure ip_002, poi di nuovo ip_001", r.Testo);
        Assert.Equal(3, r.Sostituzioni.Count);
        Assert.All(r.Sostituzioni, s => Assert.Equal(TipoInfrastruttura.Ip, s.Tipo));
    }

    [Fact]
    public void Ip_con_porta()
    {
        var (_, a) = Nuovo();
        Assert.Equal("ip_001,1433 e ip_001:1433", a.Anonimizza("10.0.0.5,1433 e 10.0.0.5:1433").Testo);
    }

    [Theory]
    [InlineData("999.1.1.1")]
    [InlineData("10.0.256.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("a.1.2.3.4")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.0")]
    [InlineData("010.1.1.1")]
    [InlineData("versione 1.2.3.4")]
    [InlineData("v1.2.3.4")]
    [InlineData("Build 5.6.7.8")]
    public void Ip_non_validi_o_da_ignorare_non_si_toccano(string testo) => Invariato(testo);

    [Fact]
    public void Ip_non_valido_genera_un_avviso()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("server 999.1.1.1");
        Assert.Single(r.Avvisi);
        Assert.Contains("999.1.1.1", r.Avvisi[0]);
    }

    // ───────────── Cose che NON vanno toccate ─────────────

    [Theory]
    [InlineData("SELECT dbo.tabella.colonna FROM dbo.tabella")]
    [InlineData("SELECT t.no, t.at FROM master.dbo.sysobjects t")]
    [InlineData("Versione 15.0.4000")]
    [InlineData("Microsoft SQL Server 15.0.4000.1 (X64)")]
    [InlineData("@@VERSION = 15.0.4000")]
    [InlineData("SELECT 1.5, 2.25, 10.0 AS x")]
    [InlineData(@"CONTOSO\mario")]
    [InlineData(@"LIKE '%\%'")]
    [InlineData("UPDATE t SET Host = 'abc'")]
    [InlineData("1 / 2 / 3 e /* commento */")]
    [InlineData("'SELECT ''a'' + ''b'''")]
    public void Testi_che_non_sono_infrastruttura_restano_identici(string testo) => Invariato(testo);

    [Fact]
    public void Percorso_di_sistema_senza_nomi_propri_resta_identico()
    {
        Invariato(@"C:\Windows\System32\Logs");
        Invariato(@"C:\Program Files\Microsoft SQL Server\MSSQL\DATA\master.mdf");
        Invariato(@"\\.\pipe\sql\query");
    }

    // ───────────── Email ─────────────

    [Fact]
    public void Email_stessa_con_maiuscole_diverse()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("scrivi a mario.rossi@contoso.com o a MARIO.ROSSI@CONTOSO.COM, grazie");
        Assert.Equal("scrivi a mail_001@example.invalid o a mail_001@example.invalid, grazie", r.Testo);
        Assert.All(r.Sostituzioni, s => Assert.Equal(TipoInfrastruttura.Email, s.Tipo));
    }

    [Fact]
    public void Email_in_un_parametro_di_dbmail()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("EXEC msdb.dbo.sp_send_dbmail @recipients = 'a.bianchi@azienda.it;b.verdi@azienda.it'");
        Assert.Equal("EXEC msdb.dbo.sp_send_dbmail @recipients = 'mail_001@example.invalid;mail_002@example.invalid'", r.Testo);
    }

    // ───────────── URL e host ─────────────

    [Fact]
    public void Url_sostituisce_host_e_avvisa_su_percorso_e_parametri()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("https://sql01.contoso.local:8443/api/v1?x=1");
        Assert.Equal("https://srv_01.dom_01.local:8443/api/v1?x=1", r.Testo);
        Assert.Single(r.Avvisi);
        Assert.Equal(TipoInfrastruttura.Url, r.Sostituzioni.Single().Tipo);
    }

    [Fact]
    public void Url_semplice_non_avvisa_e_lascia_il_punto_finale()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("vai su https://www.contoso.com/.");
        Assert.Equal("vai su https://www.dom_01.com/.", r.Testo);
        Assert.Empty(r.Avvisi);

        Assert.Equal("scrivi a https://dom_01.com.", Nuovo().Analizzatore.Anonimizza("scrivi a https://contoso.com.").Testo);
    }

    [Fact]
    public void Url_locale_resta_e_credenziali_nell_url_non_vengono_salvate()
    {
        var (mappa, a) = Nuovo();
        Invariato("http://localhost:5000/health");

        var r = a.Anonimizza("ftp://mario:SegretoInventato@files.contoso.com/dati");
        Assert.Equal("ftp://usr_001:***@srv_01.dom_01.com/dati", r.Testo);
        Assert.DoesNotContain(mappa.Voci, v => v.Originale == "SegretoInventato");
        Assert.Contains(r.Avvisi, x => x.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Host_fqdn_nel_testo_condivide_il_dominio_con_Active_Directory()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Dominio, "CONTOSO");

        var r = a.Anonimizza("il server sql01.contoso.local risponde");

        Assert.Equal("il server srv_01.dom_01.local risponde", r.Testo);
        Assert.Equal(TipoInfrastruttura.Host, r.Sostituzioni.Single().Tipo);
    }

    [Fact]
    public void Host_dello_stesso_server_in_forma_corta_e_in_unc_coincide()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"ping sql01.contoso.local e \\SQL01\Condivisa\a.bak");
        Assert.Equal(@"ping srv_01.dom_01.local e \\srv_01\dir_001\file_001.bak", r.Testo);
    }

    // ───────────── Stringhe di connessione ─────────────

    [Fact]
    public void Stringa_di_connessione_server_istanza_porta_e_database()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza(@"Server=SRVSQL01\ISTANZA1,1433;Database=Vendite;User ID=sa;Password=XXXX;");
        Assert.Equal(@"Server=srv_01\srv_02,1433;Database=db_01;User ID=sa;Password=XXXX;", r.Testo);
        Assert.Contains(r.Avvisi, x => x.Contains("credenziali"));
    }

    [Fact]
    public void Stringa_di_connessione_con_ip_e_database_di_sistema()
    {
        var (_, a) = Nuovo();
        var r = a.Anonimizza("Data Source=10.0.0.5;Initial Catalog=master;Integrated Security=SSPI");
        Assert.Equal("Data Source=ip_001;Initial Catalog=master;Integrated Security=SSPI", r.Testo);
    }

    [Fact]
    public void Stringa_di_connessione_locale_resta()
    {
        Invariato(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true");
        Invariato("Server=localhost;Trusted_Connection=yes");
    }

    // ───────────── Stabilità, idempotenza, ripristino ─────────────

    [Fact]
    public void Analizzare_di_nuovo_il_risultato_non_cambia_nulla()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");
        var primo = a.Anonimizza(@"copy \\SRVBACKUP\Condivisa\Vendite_FULL.bak a 10.0.0.5 e mario@contoso.com, https://sql01.contoso.local/x");
        var voci = mappa.Voci.Count;

        var secondo = a.Anonimizza(primo.Testo);

        Assert.Equal(primo.Testo, secondo.Testo);
        Assert.Empty(secondo.Sostituzioni);
        Assert.Equal(voci, mappa.Voci.Count);
    }

    [Fact]
    public void Andata_e_ritorno_su_un_testo_con_tutti_i_tipi()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");
        mappa.Ottieni(Ruolo.Dominio, "contoso");
        var originale =
            @"BACKUP DATABASE Vendite TO DISK = N'\\SRVBACKUP\Condivisa\Vendite_FULL_20260101.bak' " +
            @"-- by mario.rossi@contoso.com su 10.1.2.3, vedi https://sql01.contoso.local/x " +
            @"Server=SRVSQL01\ISTANZA1,1433;Database=Vendite;Integrated Security=SSPI " +
            @"e D:\Archivio Rossi\Dati\Vendite_Dati.mdf";

        var anon = a.Anonimizza(originale);
        Assert.DoesNotContain("SRVBACKUP", anon.Testo);
        Assert.DoesNotContain("mario.rossi", anon.Testo);
        Assert.DoesNotContain("10.1.2.3", anon.Testo);
        Assert.DoesNotContain("Condivisa", anon.Testo);
        Assert.DoesNotContain("Rossi", anon.Testo);

        var ripristino = a.Ripristina(anon.Testo);

        Assert.Equal(originale, ripristino.Testo);
        Assert.Empty(ripristino.NonTrovati);
        Assert.NotEmpty(ripristino.Sostituzioni);
    }

    [Fact]
    public void Ripristino_riconosce_nomi_composti_e_maiuscole_diverse()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");
        mappa.Ottieni(Ruolo.Cartella, "Condivisa");

        var r = a.Ripristina(@"DB_01_full.BAK in D:\DIR_001\db_01\x e Db_01.");

        Assert.Equal(@"Vendite_full.BAK in D:\Condivisa\Vendite\x e Vendite.", r.Testo);
    }

    [Fact]
    public void Ripristino_segnala_i_nomi_senza_corrispondenza_e_non_li_tocca()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");

        var r = a.Ripristina("db_01, dir_099 e ip_007 e tbl_001");

        Assert.Equal("Vendite, dir_099 e ip_007 e tbl_001", r.Testo);
        Assert.Equal(["dir_099", "ip_007"], r.NonTrovati);
    }

    [Fact]
    public void Ripristino_non_scambia_un_prefisso_per_un_nome_piu_lungo()
    {
        var (mappa, a) = Nuovo();
        mappa.Ottieni(Ruolo.Database, "Vendite");
        var r = a.Ripristina("mydb_01 e db_011");
        Assert.Equal("mydb_01 e db_011", r.Testo);
    }

    [Fact]
    public void Ripristino_di_una_email()
    {
        var (_, a) = Nuovo();
        var anon = a.Anonimizza("a.bianchi@azienda.it");
        Assert.Equal("a.bianchi@azienda.it", a.Ripristina(anon.Testo).Testo);
    }

    [Fact]
    public void Le_posizioni_sono_quelle_del_testo_originale()
    {
        var (_, a) = Nuovo();
        var testo = "ip 10.0.0.1 e mail a@b.com";
        var r = a.Anonimizza(testo);
        foreach (var s in r.Sostituzioni)
            Assert.Equal(s.Originale, testo.Substring(s.Posizione, s.Originale.Length));
    }

    [Fact]
    public void Testo_vuoto_o_senza_nulla_da_fare()
    {
        var (_, a) = Nuovo();
        Assert.Equal("", a.Anonimizza("").Testo);
        Assert.Equal("niente di strano", a.Anonimizza("niente di strano").Testo);
        Assert.Equal("", a.Ripristina("").Testo);
    }

    [Fact]
    public void I_nomi_nuovi_hanno_lo_stile_del_generatore()
    {
        Assert.Equal("dir_001", GeneratoreNomi.Formatta(Ruolo.Cartella, 1));
        Assert.Equal("file_012", GeneratoreNomi.Formatta(Ruolo.File, 12));
        Assert.Equal("ip_001", GeneratoreNomi.Formatta(Ruolo.Ip, 1));
        Assert.Equal("mail_001@example.invalid", GeneratoreNomi.Formatta(Ruolo.Email, 1));
    }
}
