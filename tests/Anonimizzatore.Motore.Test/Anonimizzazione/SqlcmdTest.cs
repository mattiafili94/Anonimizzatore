using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Righe SQLCMD (:setvar, :connect, :r, !!, GO n) e variabili $(...).</summary>
public class SqlcmdTest
{
    // --- divisione ---

    [Fact]
    public void Le_righe_sqlcmd_si_separano_dal_tsql_e_rimesse_in_fila_danno_l_originale()
    {
        var sql = ":setvar A 1\nSELECT 1;\nGO\n  :connect X\n/* nota\n:setvar B 2\n*/\nSELECT 2;\n::fn_x();\nGO 3\n";

        var segmenti = Sqlcmd.Dividi(sql);

        Assert.Equal(sql, string.Concat(segmenti.Select(s => s.Testo)));
        Assert.Equal(
            [(":setvar A 1\n", true, 1), ("SELECT 1;\nGO\n", false, 2), ("  :connect X\n", true, 4),
             ("/* nota\n:setvar B 2\n*/\nSELECT 2;\n::fn_x();\n", false, 5), ("GO 3\n", true, 10)],
            segmenti.Select(s => (s.Testo, s.Sqlcmd, s.RigaIniziale)));
    }

    [Theory]
    [InlineData("SELECT 1;\nGO\nSELECT 2;")]            // GO semplice resta T-SQL
    [InlineData("SELECT ':setvar' AS x;")]               // non all'inizio di una riga
    [InlineData("SELECT 1\n:\nSELECT 2")]                // due punti da solo
    [InlineData("")]
    public void Testo_senza_righe_sqlcmd_e_un_solo_blocco_di_tsql(string sql)
    {
        var segmenti = Sqlcmd.Dividi(sql);

        Assert.All(segmenti, s => Assert.False(s.Sqlcmd));
        Assert.Equal(sql, string.Concat(segmenti.Select(s => s.Testo)));
    }

    [Fact]
    public void Righe_con_a_capo_windows()
    {
        var segmenti = Sqlcmd.Dividi(":setvar A 1\r\nSELECT 1;\r\n!!dir\r\n");

        Assert.Equal([":setvar A 1\r\n", "SELECT 1;\r\n", "!!dir\r\n"], segmenti.Select(s => s.Testo));
    }

    // --- :setvar ---

    [Theory]
    [InlineData(":setvar DbName Vendite", ":setvar DbName db_01")]
    [InlineData(":setvar NomeDatabase Vendite", ":setvar NomeDatabase db_01")]
    [InlineData(":setvar ServerRemoto SRVREMOTO", ":setvar ServerRemoto srv_01")]
    [InlineData(":setvar Istanza SRVSQL02\\ISTANZA1", ":setvar Istanza srv_01\\srv_02")]
    [InlineData(":setvar SchemaApp Vendite", ":setvar SchemaApp sch_01")]
    [InlineData(":setvar UtenteApp ACMEDOM\\mario", ":setvar UtenteApp dom_01\\usr_001")]
    [InlineData(":setvar PasswordApp Segr3ta!", ":setvar PasswordApp <password>")]
    [InlineData(":setvar Pwd \"Segr3ta con spazi\"", ":setvar Pwd \"<password>\"")]
    [InlineData(":setvar NomeJob Chiusura", ":setvar NomeJob job_001")]
    [InlineData(":setvar CartellaDati D:\\Dati\\Acme", ":setvar CartellaDati D:\\Dati\\dir_001")]
    [InlineData(":setvar Backup \"\\\\SRVBACKUP\\Condivisa\\x.bak\"", ":setvar Backup \"\\\\srv_01\\dir_001\\file_001.bak\"")]
    [InlineData(":setvar Contatto dba@acme.example", ":setvar Contatto mail_001@example.invalid")]
    public void Setvar_il_valore_si_anonimizza_secondo_il_nome_della_variabile(string riga, string atteso)
    {
        var anon = Anon(riga + "\nSELECT 1;");

        Assert.Equal(atteso + "\nSELECT 1;", anon);
    }

    [Theory]
    [InlineData(":setvar Versione 3")]                       // niente nome
    [InlineData(":setvar DbName")]                           // variabile senza valore (la elimina)
    [InlineData(":setvar DbName $(Altra)")]                  // il valore è un'altra variabile
    [InlineData(":setvar DbName master")]                    // database di sistema
    [InlineData(":setvar ServerLocale localhost")]
    [InlineData(":setvar UtenteSa sa")]
    [InlineData(":setvar Cartella $(Base)\\Dati")]
    public void Setvar_valori_che_non_sono_nomi_da_cambiare(string riga) => Assert.Equal(riga + "\nSELECT 1;", Anon(riga + "\nSELECT 1;"));

    [Fact]
    public void Setvar_con_nome_non_riconosciuto_cerca_i_nomi_gia_noti()
    {
        var anon = Anon(":setvar Tabella Ordini\nSELECT * FROM dbo.Ordini;");

        Assert.Equal(":setvar Tabella tbl_001\nSELECT * FROM dbo.tbl_001;", anon);
    }

    [Fact]
    public void Setvar_rispetta_la_blacklist_e_la_matrice()
    {
        // Database in chiaro per matrice: resta Vendite; il server contiene una parola in blacklist: viene anonimizzato.
        var profilo = NuovoProfilo("Acme");
        profilo.Matrice[Ruolo.Database] = Motore.Profili.Trattamento.InChiaro;

        var anon = Anon(":setvar DbName Vendite\n:setvar ServerApp SrvAcme", profilo: profilo);

        Assert.Equal(":setvar DbName Vendite\n:setvar ServerApp srv_01", anon);
    }

    // --- :connect, :r, !! ---

    [Fact]
    public void Connect_server_utente_e_password()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["SELECT 1;\n:connect SRVSQL01\\ISTANZA1 -l 30 -U ACMEDOM\\svc_deploy -P Pwd123!\nSELECT 2;\n:connect SRVSQL02 -U sa -P x\n"],
            NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.Equal("SELECT 1;\n:connect srv_01\\srv_02 -l 30 -U dom_01\\usr_001 -P <password>\nSELECT 2;\n:connect srv_03 -U sa -P <password>\n",
            esito.Testo);
        Assert.Equal([2, 4], esito.Avvisi.Where(a => a.Messaggio.Contains("password")).Select(a => a.Riga));
    }

    [Fact]
    public void Connect_a_un_server_locale_resta()
    {
        Assert.Equal(":connect localhost\nSELECT 1;", Anon(":connect localhost\nSELECT 1;"));
    }

    [Fact]
    public void File_di_r_out_ed_error()
    {
        var anon = Anon("""
            :r D:\Script\Acme\schema.sql
            :r "\\SRVBACKUP\Condivisa\a.sql"
            :out risultati.txt
            :error $(Cartella)\errori.txt
            :r $(Base)\x.sql
            SELECT 1;
            """);

        Assert.Equal("""
            :r D:\Script\dir_001\file_001.sql
            :r "\\srv_01\dir_002\file_002.sql"
            :out file_003.txt
            :error $(Cartella)\errori.txt
            :r $(Base)\x.sql
            SELECT 1;
            """, anon);
    }

    [Fact]
    public void Riga_di_comando_con_sqlcmd_e_opzioni()
    {
        var anon = Anon("!!sqlcmd -S SRVSQL01 -d Vendite -U ACMEDOM\\svc -P Segr3ta -i D:\\Script\\Acme\\x.sql -o \"D:\\Log\\Acme\\out.txt\"\nSELECT 1;");

        Assert.Equal("!!sqlcmd -S srv_01 -d db_01 -U dom_01\\usr_001 -P <password> -i D:\\Script\\dir_001\\file_001.sql -o \"D:\\Log\\dir_001\\file_002.txt\"\nSELECT 1;",
            anon);
    }

    [Fact]
    public void Altri_comandi_sqlcmd_non_cambiano()
    {
        var sql = ":on error exit\n:quit\nSELECT 1;\nGO 5\n:exit\n";

        Assert.Equal(sql, Anon(sql));
    }

    [Fact]
    public void Le_opzioni_di_sqlcmd_valgono_anche_dentro_xp_cmdshell_e_nei_job()
    {
        var anon = Anon("""
            EXEC xp_cmdshell 'sqlcmd -S SRVSQL01 -d Vendite -U sa -P Segr3ta -Q "SELECT 1" -s ";"';
            EXEC msdb.dbo.sp_add_jobstep @job_name = N'J', @step_name = N'S', @subsystem = N'CmdExec',
                @command = N'bcp Vendite.dbo.Ordini out D:\Export\o.dat -S SRVSQL01 -T -U ACMEDOM\svc -P Segr3ta';
            """);

        Assert.Contains("-S srv_01 -d db_01 -U sa -P <password> -Q \"SELECT 1\" -s \";\"", anon);
        Assert.Contains("-S srv_01 -T -U dom_01\\usr_001 -P <password>", anon);
        Assert.DoesNotContain("Segr3ta", anon);
    }

    // --- variabili $(...) nei nomi ---

    [Fact]
    public void Le_variabili_sqlcmd_dentro_i_nomi_non_si_toccano()
    {
        var anon = Anon("USE [$(DbName)];\nGO\nSELECT * FROM [$(ServerRemoto)].[$(DbName)].dbo.Ordini;");

        Assert.Equal("USE [$(DbName)];\nGO\nSELECT * FROM [$(ServerRemoto)].[$(DbName)].dbo.tbl_001;", anon);
    }

    [Fact]
    public void Variabile_sqlcmd_senza_parentesi_quadre_va_in_modalita_di_riserva_ma_il_resto_si_anonimizza()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["USE $(DbName);\nGO\nSELECT * FROM $(DbName).dbo.Ordini;"], NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.Equal("USE $(DbName);\nGO\nSELECT * FROM $(DbName).dbo.tbl_001;", esito.Testo);
        Assert.Contains(esito.Avvisi, a => a.Messaggio.Contains("modalità di riserva"));
    }

    // --- GO n e blocchi indipendenti ---

    [Fact]
    public void Go_con_il_conteggio_non_manda_in_errore_il_blocco_prima()
    {
        var esito = AnonimizzatoreScript.Anonimizza(["INSERT INTO dbo.Ordini (Id) VALUES (1);\nGO 100\n"], NuovoProfilo(), new MappaCliente()).Script[0];

        Assert.Equal("INSERT INTO dbo.tbl_001 (Id) VALUES (1);\nGO 100\n", esito.Testo);
        Assert.Empty(esito.Avvisi);
    }

    [Fact]
    public void Un_blocco_rotto_non_rovina_gli_altri_e_la_riga_dell_avviso_e_quella_dello_script()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            [":setvar X 1\n:on error exit\nSELECT * FROM dbo.Ordini WHERE (\n:setvar Y 2\nSELECT * FROM dbo.Righe;"],
            NuovoProfilo(), new MappaCliente()).Script[0];

        // Il blocco valido prende i numeri per primo (Righe = tbl_001); quello rotto dopo (Ordini = tbl_002).
        Assert.Equal(":setvar X 1\n:on error exit\nSELECT * FROM dbo.tbl_002 WHERE (\n:setvar Y 2\nSELECT * FROM dbo.tbl_001;", esito.Testo);
        var avviso = Assert.Single(esito.Avvisi);
        Assert.Equal(3, avviso.Riga);
    }

    // --- esempio completo ---

    [Fact]
    public void Esempio_completo_nessun_dato_del_cliente_resta()
    {
        var anon = Anon(LeggiEsempio("sqlcmd-acme.sql"), profilo: NuovoProfilo("Acme"));

        foreach (var vietata in new[] { "Vendite", "SRVREMOTO", "SRVSQL01", "Acme", "ACMEDOM", "svc_deploy", "Segr3ta", "Pwd123", "Ordini", "schema.sql", "controllo" })
            Assert.DoesNotContain(vietata, anon, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USE [$(DbName)];", anon);
        Assert.Contains("GO 5", anon);
    }

    [Fact]
    public void Esempio_completo_andata_e_ritorno_tranne_le_password()
    {
        var sql = LeggiEsempio("sqlcmd-acme.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        var atteso = sql.Replace("Segr3ta!", "<password>").Replace("Pwd123!", "<password>").Replace("Segr3taSa", "<password>");
        Assert.Equal(atteso, RipristinaPerProva(anon, mappa));
    }
}
