using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>SQL dinamico: stringhe che contengono T-SQL, dichiarazioni di parametri, SQL costruito a pezzi, job di SQL Agent.</summary>
public class SqlDinamicoTest
{
    // --- SQL completo e valido ---

    [Fact]
    public void Exec_con_sql_valido_viene_anonimizzato_come_uno_script()
    {
        var anon = Anon("EXEC (N'SELECT * FROM dbo.Ordini WHERE Id = 1');");

        Assert.Equal("EXEC (N'SELECT * FROM dbo.tbl_001 WHERE Id = 1');", anon);
    }

    [Fact]
    public void Gli_apici_raddoppiati_si_tolgono_per_leggere_e_si_rimettono()
    {
        var anon = Anon("EXEC (N'UPDATE dbo.Ordini SET Stato = ''Chiuso'' WHERE Nota = ''L''''Aquila''');");

        Assert.Equal("EXEC (N'UPDATE dbo.tbl_001 SET Stato = ''Chiuso'' WHERE Nota = ''L''''Aquila''');", anon);
    }

    [Fact]
    public void Sql_dinamico_dentro_sql_dinamico()
    {
        var anon = Anon("EXEC (N'EXEC (''DELETE FROM dbo.Ordini WHERE Id = ''''A1'''''')');");

        Assert.Equal("EXEC (N'EXEC (''DELETE FROM dbo.tbl_001 WHERE Id = ''''A1'''''')');", anon);
    }

    [Fact]
    public void Troppi_livelli_non_mandano_in_loop_e_non_perdono_il_resto()
    {
        // Sei livelli di EXEC dentro EXEC: oltre la profondità massima l'ultimo resta com'è, ma niente crash.
        var sql = "SELECT * FROM dbo.Ordini";
        for (var livello = 0; livello < 6; livello++) sql = $"EXEC (N'{sql.Replace("'", "''")}')";

        var esito = AnonimizzatoreScript.Anonimizza([sql], NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.True(esito.Anonimizzato);
        Assert.Contains("tbl_001", esito.Testo);
    }

    [Fact]
    public void Una_stringa_assegnata_a_una_variabile_e_poi_eseguita()
    {
        var anon = Anon("DECLARE @s nvarchar(max) = N'DELETE FROM dbo.Ordini'; EXEC (@s);");

        Assert.Equal("DECLARE @s nvarchar(max) = N'DELETE FROM dbo.tbl_001'; EXEC (@s);", anon);
    }

    [Fact]
    public void I_commenti_dentro_il_sql_dinamico_diventano_segnaposto()
    {
        var anon = Anon("EXEC (N'SELECT * FROM dbo.Ordini -- password del cliente: Segreta\nWHERE Id = 1 /* nota riservata */');");

        Assert.DoesNotContain("Segreta", anon);
        Assert.DoesNotContain("riservata", anon);
        Assert.Matches(@"N'SELECT \* FROM dbo\.tbl_001 /\* c001 \*/\nWHERE Id = 1 /\* c002 \*/'", anon);
    }

    [Fact]
    public void Una_stringa_che_e_un_nome_o_un_segreto_non_e_sql()
    {
        // 'Select Mario' per caso si legge come T-SQL (SELECT Mario), ma è il nome di un utente in EXECUTE AS.
        var anon = Anon("EXECUTE AS USER = 'Select Mario';");

        Assert.Equal("EXECUTE AS USER = 'usr_001';", anon);
    }

    // --- sp_executesql ---

    [Fact]
    public void Sp_executesql_nomi_dei_parametri_uguali_nella_stringa_nella_dichiarazione_e_negli_argomenti()
    {
        var anon = Anon("""
            EXEC sp_executesql N'SELECT * FROM dbo.Ordini WHERE CodiceAcme = @CodiceAcme AND Stato = @Stato',
                N'@CodiceAcme varchar(20), @Stato int',
                @CodiceAcme = 'A1', @Stato = 1;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal("""
            EXEC sp_executesql N'SELECT * FROM dbo.tbl_001 WHERE col_x_001 = @v_vc20_001 AND Stato = @Stato',
                N'@v_vc20_001 varchar(20), @Stato int',
                @v_vc20_001 = 'A1', @Stato = 1;
            """, anon);
    }

    [Fact]
    public void Sp_executesql_con_stmt_e_params_con_nome_e_parametro_output()
    {
        var anon = Anon("""
            EXEC sp_executesql @stmt = N'SELECT @n = COUNT(*) FROM dbo.Ordini', @params = N'@n int OUTPUT', @n = @conta OUTPUT;
            """);

        Assert.Equal("""
            EXEC sp_executesql @stmt = N'SELECT @n = COUNT(*) FROM dbo.tbl_001', @params = N'@n int OUTPUT', @n = @conta OUTPUT;
            """, anon);
    }

    // --- SQL costruito a pezzi ---

    [Fact]
    public void Sql_costruito_a_pezzi_sostituzione_per_dizionario_e_avviso()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["SELECT * FROM dbo.Ordini; DECLARE @s nvarchar(max) = N'SELECT * FROM dbo.' + QUOTENAME(@t) + N' WHERE Ordini.Id = 1 ORDER BY Id'; EXEC (@s);"],
            NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.Equal(
            "SELECT * FROM dbo.tbl_001; DECLARE @s nvarchar(max) = N'SELECT * FROM dbo.' + QUOTENAME(@t) + N' WHERE tbl_001.Id = 1 ORDER BY Id'; EXEC (@s);",
            esito.Testo);
        var avviso = Assert.Single(esito.Avvisi, a => a.Messaggio.Contains("a pezzi"));
        Assert.Equal(1, avviso.Riga);
    }

    [Fact]
    public void Sql_con_errori_di_sintassi_nella_stringa_sostituisce_i_nomi_noti()
    {
        var anon = Anon("SELECT * FROM dbo.Ordini; EXEC (N'SELECT * FORM Ordini WHERE');");

        Assert.Equal("SELECT * FROM dbo.tbl_001; EXEC (N'SELECT * FORM tbl_001 WHERE');", anon);
    }

    [Fact]
    public void Il_dizionario_rispetta_parole_intere_parentesi_e_variabili()
    {
        var anon = Anon("""
            SELECT * FROM dbo.Ordini;
            DECLARE @s nvarchar(max) = N'SELECT [Ordini].Id, OrdiniArchivio.Id, @Ordini, Ordini2 FROM Ordini WHERE ' + @x;
            """);

        Assert.Contains("N'SELECT [tbl_001].Id, OrdiniArchivio.Id, @Ordini, Ordini2 FROM tbl_001 WHERE '", anon);
    }

    [Fact]
    public void Una_stringa_che_e_solo_il_nome_di_un_oggetto()
    {
        var anon = Anon("""
            IF OBJECT_ID(N'dbo.Ordini', N'U') IS NOT NULL DROP TABLE dbo.Ordini;
            DECLARE @tabella sysname = N'Ordini', @nota varchar(50) = 'Ordini elaborati';
            SELECT COL_LENGTH('dbo.Ordini', 'CodiceAcme'), CodiceAcme FROM dbo.Ordini;
            """, profilo: NuovoProfilo("Acme"));

        Assert.Equal("""
            IF OBJECT_ID(N'dbo.tbl_001', N'U') IS NOT NULL DROP TABLE dbo.tbl_001;
            DECLARE @tabella sysname = N'tbl_001', @nota varchar(50) = 'tbl_001 elaborati';
            SELECT COL_LENGTH('dbo.tbl_001', 'col_x_001'), col_x_001 FROM dbo.tbl_001;
            """, anon);
    }

    [Fact]
    public void Stringhe_che_non_sono_sql_restano_uguali()
    {
        var sql = "SELECT 'Prova', N'Save completed', 'x', 'A1', 'Open', N'Elenco', ''; PRINT 'Update completato';";

        Assert.Equal(sql, Anon(sql));
    }

    // --- job di SQL Agent e comandi ---

    [Fact]
    public void Job_nome_proprietario_database_e_passo_tsql()
    {
        var anon = Anon("""
            EXEC msdb.dbo.sp_add_job @job_name = N'Chiusura ordini', @enabled = 1, @owner_login_name = N'ACMEDOM\svc_agent';
            EXEC msdb.dbo.sp_add_jobstep @job_name = N'Chiusura ordini', @step_name = N'Passo', @subsystem = N'TSQL',
                @database_name = N'Vendite', @command = N'DELETE FROM dbo.Ordini WHERE Stato = ''X''';
            EXEC msdb.dbo.sp_add_jobserver @job_name = N'Chiusura ordini', @server_name = N'(local)';
            EXEC msdb.dbo.sp_start_job N'Chiusura ordini';
            """);

        Assert.Equal("""
            EXEC msdb.dbo.sp_add_job @job_name = N'job_001', @enabled = 1, @owner_login_name = N'dom_01\usr_001';
            EXEC msdb.dbo.sp_add_jobstep @job_name = N'job_001', @step_name = N'Passo', @subsystem = N'TSQL',
                @database_name = N'db_01', @command = N'DELETE FROM dbo.tbl_001 WHERE Stato = ''X''';
            EXEC msdb.dbo.sp_add_jobserver @job_name = N'job_001', @server_name = N'(local)';
            EXEC msdb.dbo.sp_start_job N'job_001';
            """, anon);
    }

    [Fact]
    public void Passo_di_job_con_riga_di_comando_e_xp_cmdshell_sono_testo_libero()
    {
        var anon = Anon("""
            EXEC msdb.dbo.sp_add_jobstep @job_name = N'J', @step_name = N'Copia', @subsystem = N'CmdExec',
                @command = N'robocopy D:\Export \\SRVBACKUP\Condivisa\Export /E /R:3';
            EXEC xp_cmdshell 'copy D:\Dati\a.bak \\SRVBACKUP\Condivisa\ /Y';
            """);

        Assert.Contains(" /E /R:3'", anon);   // le opzioni non diventano cartelle
        Assert.Contains(" /Y'", anon);
        Assert.DoesNotContain("SRVBACKUP", anon);
        Assert.DoesNotContain("Condivisa", anon);
    }

    // --- esempio completo ---

    [Fact]
    public void Esempio_completo_niente_in_chiaro_tranne_i_testi_liberi_e_le_stringhe_a_pezzi()
    {
        var anon = Anon(LeggiEsempio("sql-dinamico-acme.sql"), profilo: NuovoProfilo("Acme"));

        foreach (var vietata in new[] { "uspChiudiOrdini", "Ordini", "OrdiniArchivio", "CodiceAcme", "ACMEDOM", "svc_agent",
                     "Chiusura ordini", "Vendite", "SRVBACKUP", "Condivisa" })
            Assert.DoesNotContain(vietata, anon, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Esempio_completo_andata_e_ritorno()
    {
        var sql = LeggiEsempio("sql-dinamico-acme.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa, NuovoProfilo("Acme"));

        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void Nel_ripristino_un_nome_con_spazi_dentro_una_stringa_non_prende_le_parentesi_quadre()
    {
        var mappa = new MappaCliente();
        Anon("EXEC msdb.dbo.sp_start_job @job_name = N'Chiusura ordini notturna'; SELECT * FROM [Vendite 2026].dbo.Ordini;", mappa);

        var risposta = "EXEC sp_start_job N'job_001', @x = 'job_001';\nSELECT * FROM db_01.dbo.tbl_001;";
        var esito = new Motore.Deanonimizzazione.DeAnonimizzatore(mappa).DeAnonimizza(risposta);

        // Nelle stringhe il nome è un valore; nel codice SQL è un identificatore e va tra parentesi.
        Assert.Equal("EXEC sp_start_job N'Chiusura ordini notturna', @x = 'Chiusura ordini notturna';\nSELECT * FROM [Vendite 2026].dbo.Ordini;",
            esito.Testo);
    }

    [Fact]
    public void Un_commento_con_a_capo_dentro_la_stringa_torna_identico()
    {
        var sql = "EXEC (N'SELECT 1 -- nota\nSELECT 2');";
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        Assert.Equal("EXEC (N'SELECT 1 /* c001 */\nSELECT 2');", anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }
}
