using System.Text.Json;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Livello 2: login, utenti, ruoli, permessi, e password/SID che non devono mai uscire.</summary>
public class SicurezzaTest
{
    // --- login, utenti, domini ---

    [Fact]
    public void Login_windows_dominio_e_utente_si_separano()
    {
        var anon = Anon("CREATE LOGIN [ACMEDOM\\mario.rossi] FROM WINDOWS WITH DEFAULT_DATABASE = [Vendite];");

        Assert.Equal("CREATE LOGIN [dom_01\\usr_001] FROM WINDOWS WITH DEFAULT_DATABASE = [db_01];", anon);
    }

    [Fact]
    public void Lo_stesso_dominio_ha_lo_stesso_nome_per_utenti_diversi()
    {
        var anon = Anon("CREATE LOGIN [ACMEDOM\\mario] FROM WINDOWS; CREATE LOGIN [acmedom\\luisa] FROM WINDOWS; CREATE LOGIN [ALTRO\\mario] FROM WINDOWS;");

        Assert.Equal(
            "CREATE LOGIN [dom_01\\usr_001] FROM WINDOWS; CREATE LOGIN [dom_01\\usr_002] FROM WINDOWS; CREATE LOGIN [dom_02\\usr_001] FROM WINDOWS;",
            anon);
    }

    [Fact]
    public void Login_e_utente_con_lo_stesso_nome_hanno_lo_stesso_nome_anonimo()
    {
        var anon = Anon("CREATE LOGIN svc_vendite WITH PASSWORD = 'x'; CREATE USER svc_vendite FOR LOGIN svc_vendite;");

        Assert.Equal("CREATE LOGIN usr_001 WITH PASSWORD = '<password>'; CREATE USER usr_001 FOR LOGIN usr_001;", anon);
    }

    [Theory]
    [InlineData("CREATE LOGIN [NT AUTHORITY\\SYSTEM] FROM WINDOWS;")]
    [InlineData("CREATE LOGIN [NT SERVICE\\MSSQLSERVER] FROM WINDOWS;")]
    [InlineData("CREATE LOGIN [BUILTIN\\Administrators] FROM WINDOWS;")]
    [InlineData("ALTER LOGIN sa DISABLE;")]
    [InlineData("ALTER SERVER ROLE sysadmin ADD MEMBER [NT AUTHORITY\\SYSTEM];")]
    [InlineData("ALTER ROLE db_datareader ADD MEMBER dbo;")]
    [InlineData("GRANT SELECT ON sys.objects TO public;")]
    [InlineData("GRANT CONNECT TO guest;")]
    [InlineData("DROP LOGIN [##MS_PolicyEventProcessingLogin##];")]
    [InlineData("EXECUTE AS USER = 'dbo';")]
    public void Account_e_ruoli_di_sistema_restano_in_chiaro(string sql) => Assert.Equal(sql, Anon(sql));

    [Fact]
    public void Con_la_matrice_che_lascia_in_chiaro_il_dominio_resta_solo_l_utente_anonimizzato()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Dominio] = Trattamento.InChiaro;

        var anon = Anon("CREATE LOGIN [ACMEDOM\\mario] FROM WINDOWS;", profilo: profilo);

        Assert.Equal("CREATE LOGIN [ACMEDOM\\usr_001] FROM WINDOWS;", anon);
    }

    [Fact]
    public void Login_con_email_viene_anonimizzato_per_intero()
    {
        Assert.Equal("CREATE LOGIN [usr_001] FROM EXTERNAL PROVIDER;", Anon("CREATE LOGIN [mario@acme.example] FROM EXTERNAL PROVIDER;"));
    }

    [Fact]
    public void Utente_senza_login_schema_di_default_e_rinomina()
    {
        var anon = Anon("""
            CREATE USER Contabile WITHOUT LOGIN WITH DEFAULT_SCHEMA = Vendite;
            ALTER USER Contabile WITH NAME = Ragioniere, DEFAULT_SCHEMA = Acquisti;
            ALTER LOGIN svc_vendite WITH NAME = svc_nuovo;
            """);

        Assert.Equal("""
            CREATE USER usr_001 WITHOUT LOGIN WITH DEFAULT_SCHEMA = sch_01;
            ALTER USER usr_001 WITH NAME = usr_002, DEFAULT_SCHEMA = sch_02;
            ALTER LOGIN usr_003 WITH NAME = usr_004;
            """, anon);
    }

    // --- ruoli ---

    [Fact]
    public void Un_ruolo_dichiarato_ha_lo_stesso_nome_in_grant_e_membri()
    {
        var anon = Anon("""
            CREATE ROLE ruolo_contabilita AUTHORIZATION dbo;
            ALTER ROLE ruolo_contabilita ADD MEMBER Mario;
            GRANT SELECT ON dbo.Ordini TO ruolo_contabilita;
            GRANT SELECT ON dbo.Ordini TO Mario;
            ALTER ROLE ruolo_contabilita WITH NAME = ruolo_ragioneria;
            DROP ROLE ruolo_ragioneria;
            """);

        Assert.Equal("""
            CREATE ROLE rol_001 AUTHORIZATION dbo;
            ALTER ROLE rol_001 ADD MEMBER usr_001;
            GRANT SELECT ON dbo.tbl_001 TO rol_001;
            GRANT SELECT ON dbo.tbl_001 TO usr_001;
            ALTER ROLE rol_001 WITH NAME = rol_002;
            DROP ROLE rol_002;
            """, anon);
    }

    [Fact]
    public void Il_ruolo_creato_in_un_altro_script_o_in_una_sessione_precedente_resta_un_ruolo()
    {
        var mappa = new MappaCliente();
        var esito = AnonimizzatoreScript.Anonimizza(
            ["GRANT SELECT ON dbo.Ordini TO ruolo_contabilita;", "CREATE ROLE ruolo_contabilita;"], NuovoProfilo(), mappa);

        Assert.Equal("GRANT SELECT ON dbo.tbl_001 TO rol_001;", esito.Script[0].Testo);
        Assert.Equal("CREATE ROLE rol_001;", esito.Script[1].Testo);

        // Dopo, in una chiamata nuova, la mappa sa già che è un ruolo.
        Assert.Equal("GRANT SELECT ON dbo.tbl_001 TO rol_001;", Anon("GRANT SELECT ON dbo.Ordini TO ruolo_contabilita;", mappa));
    }

    [Fact]
    public void Ruoli_di_server_e_ruoli_applicativi()
    {
        var anon = Anon("""
            CREATE SERVER ROLE ruolo_server;
            ALTER SERVER ROLE ruolo_server ADD MEMBER [ACMEDOM\mario];
            ALTER SERVER ROLE sysadmin ADD MEMBER svc_vendite;
            CREATE APPLICATION ROLE app_contabile WITH PASSWORD = 'p4ss', DEFAULT_SCHEMA = Vendite;
            """);

        Assert.Equal("""
            CREATE SERVER ROLE rol_001;
            ALTER SERVER ROLE rol_001 ADD MEMBER [dom_01\usr_001];
            ALTER SERVER ROLE sysadmin ADD MEMBER usr_002;
            CREATE APPLICATION ROLE rol_002 WITH PASSWORD = '<password>', DEFAULT_SCHEMA = sch_01;
            """, anon);
    }

    // --- permessi ---

    [Fact]
    public void Grant_deny_e_revoke_su_oggetti_schemi_colonne_e_database()
    {
        var anon = Anon("""
            CREATE PROCEDURE Vendite.usp_OrdiniCliente AS SELECT 1;
            GO
            GRANT SELECT, UPDATE ON OBJECT::Vendite.Ordini TO Mario WITH GRANT OPTION;
            GRANT EXECUTE ON Vendite.usp_OrdiniCliente TO Mario AS Luisa;
            DENY DELETE ON Vendite.Ordini TO Mario CASCADE;
            REVOKE GRANT OPTION FOR SELECT ON SCHEMA::Vendite FROM Mario CASCADE;
            GRANT SELECT (IdOrdine, Importo) ON Vendite.Ordini TO Mario;
            GRANT CONNECT ON DATABASE::Vendite TO Mario;
            GRANT IMPERSONATE ON USER::Luisa TO Mario;
            GRANT VIEW SERVER STATE TO [ACMEDOM\mario];
            """);

        Assert.Equal("""
            CREATE PROCEDURE sch_01.usp_001 AS SELECT 1;
            GO
            GRANT SELECT, UPDATE ON OBJECT::sch_01.tbl_001 TO usr_001 WITH GRANT OPTION;
            GRANT EXECUTE ON sch_01.usp_001 TO usr_001 AS usr_002;
            DENY DELETE ON sch_01.tbl_001 TO usr_001 CASCADE;
            REVOKE GRANT OPTION FOR SELECT ON SCHEMA::sch_01 FROM usr_001 CASCADE;
            GRANT SELECT (IdOrdine, Importo) ON sch_01.tbl_001 TO usr_001;
            GRANT CONNECT ON DATABASE::db_01 TO usr_001;
            GRANT IMPERSONATE ON USER::usr_002 TO usr_001;
            GRANT VIEW SERVER STATE TO [dom_01\usr_001];
            """, anon);
    }

    [Fact]
    public void Execute_si_da_su_procedure_anche_se_non_dichiarate_nello_script()
    {
        var mappa = new MappaCliente();

        var anon = Anon("GRANT EXECUTE ON dbo.usp_OrdiniCliente TO Mario; GRANT SELECT ON dbo.Ordini TO Mario;", mappa);

        Assert.Equal("GRANT EXECUTE ON dbo.usp_001 TO usr_001; GRANT SELECT ON dbo.tbl_001 TO usr_001;", anon);

        // Lo stesso nome, poi dichiarato come procedura in un altro script, ha lo stesso nome anonimo.
        Assert.Equal("CREATE PROCEDURE dbo.usp_001 AS SELECT 1;", Anon("CREATE PROCEDURE dbo.usp_OrdiniCliente AS SELECT 1;", mappa));
    }

    [Fact]
    public void Authorization_su_schema_e_proprietario_dello_schema()
    {
        var anon = Anon("""
            CREATE SCHEMA Vendite AUTHORIZATION Mario;
            GO
            ALTER AUTHORIZATION ON SCHEMA::Vendite TO [ACMEDOM\luisa];
            ALTER AUTHORIZATION ON OBJECT::Vendite.Ordini TO Mario;
            """);

        Assert.Equal("""
            CREATE SCHEMA sch_01 AUTHORIZATION usr_001;
            GO
            ALTER AUTHORIZATION ON SCHEMA::sch_01 TO [dom_01\usr_002];
            ALTER AUTHORIZATION ON OBJECT::sch_01.tbl_001 TO usr_001;
            """, anon);
    }

    // --- nomi dentro le stringhe ---

    [Fact]
    public void Execute_as_e_setuser_con_il_nome_in_una_stringa()
    {
        var anon = Anon("""
            EXECUTE AS USER = 'Mario';
            EXECUTE AS LOGIN = N'ACMEDOM\mario';
            GO
            CREATE PROCEDURE dbo.p WITH EXECUTE AS 'Mario' AS SELECT 1;
            GO
            SETUSER 'Luisa';
            EXECUTE AS USER = 'O''Brien';
            EXECUTE AS CALLER;
            """);

        // "ACMEDOM\mario" ha lo stesso utente (mario) di 'Mario': i nomi non distinguono maiuscole e minuscole.
        Assert.Equal("""
            EXECUTE AS USER = 'usr_001';
            EXECUTE AS LOGIN = N'dom_01\usr_001';
            GO
            CREATE PROCEDURE dbo.usp_001 WITH EXECUTE AS 'usr_001' AS SELECT 1;
            GO
            SETUSER 'usr_002';
            EXECUTE AS USER = 'usr_003';
            EXECUTE AS CALLER;
            """, anon);
    }

    [Fact]
    public void Procedure_di_sistema_per_la_sicurezza_posizionali_e_con_nome()
    {
        var anon = Anon("""
            EXEC sp_addlogin 'vecchio_login', 'P@ssw0rd!', 'Vendite';
            EXEC sp_addlogin @loginame = 'altro_login', @passwd = N'Segr3to', @defdb = N'Vendite';
            EXEC sp_password 'vecchia', 'nuova', 'vecchio_login';
            EXEC sp_addrolemember 'ruolo_contabilita', 'Mario';
            EXEC sp_addrolemember N'db_owner', N'vecchio_login';
            EXEC sp_addsrvrolemember @loginame = 'altro_login', @rolename = 'sysadmin';
            EXEC sp_change_users_login 'Auto_Fix', 'Mario', NULL, 'pwd-utente';
            EXEC sp_adduser 'vecchio_login', 'Mario', 'db_owner';
            EXEC sp_droplogin 'altro_login';
            """);

        Assert.Equal("""
            EXEC sp_addlogin 'usr_001', '<password>', 'db_01';
            EXEC sp_addlogin @loginame = 'usr_002', @passwd = N'<password>', @defdb = N'db_01';
            EXEC sp_password '<password>', '<password>', 'usr_001';
            EXEC sp_addrolemember 'rol_001', 'usr_003';
            EXEC sp_addrolemember N'db_owner', N'usr_001';
            EXEC sp_addsrvrolemember @loginame = 'usr_002', @rolename = 'sysadmin';
            EXEC sp_change_users_login 'Auto_Fix', 'usr_003', NULL, '<password>';
            EXEC sp_adduser 'usr_001', 'usr_003', 'db_owner';
            EXEC sp_droplogin 'usr_002';
            """, anon);
    }

    [Fact]
    public void Server_collegato_e_login_remoto()
    {
        var anon = Anon("EXEC sp_addlinkedsrvlogin 'SRVREMOTO', 'false', 'Mario', 'utente_remoto', 'pwd-remota';");

        Assert.Equal("EXEC sp_addlinkedsrvlogin 'srv_01', 'false', 'usr_001', 'usr_002', '<password>';", anon);
    }

    // --- password e SID ---

    [Fact]
    public void Password_e_sid_spariscono_e_non_finiscono_nella_mappa()
    {
        var mappa = new MappaCliente();
        var anon = Anon("""
            CREATE LOGIN svc_vendite WITH PASSWORD = N'S3gr3ta!Lunga' MUST_CHANGE,
                SID = 0x1A2B3C4D5E6F708192A3B4C5D6E7F801, CHECK_EXPIRATION = ON;
            """, mappa);

        Assert.Equal("""
            CREATE LOGIN usr_001 WITH PASSWORD = N'<password>' MUST_CHANGE,
                SID = 0x00000000000000000000000000000000, CHECK_EXPIRATION = ON;
            """, anon);

        var tutto = JsonSerializer.Serialize(mappa.Esporta());
        Assert.DoesNotContain("S3gr3ta", tutto);
        Assert.DoesNotContain("1A2B3C", tutto);
        Assert.DoesNotContain("password", tutto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hash_di_password_e_password_vecchia_e_nuova()
    {
        var anon = Anon("""
            CREATE LOGIN a1 WITH PASSWORD = 0x0200AABBCCDD HASHED;
            ALTER LOGIN a1 WITH PASSWORD = 'nuova-password' OLD_PASSWORD = 'vecchia-password', CHECK_POLICY = OFF;
            ALTER LOGIN a1 WITH PASSWORD = 'altra' UNLOCK;
            CREATE USER u1 WITH PASSWORD = 'pwd-contained', DEFAULT_SCHEMA = dbo;
            """);

        foreach (var segreto in new[] { "0200AABB", "nuova-password", "vecchia-password", "'altra'", "pwd-contained" })
            Assert.DoesNotContain(segreto, anon);
        Assert.Contains("PASSWORD = 0x00 HASHED", anon);
        Assert.Contains("PASSWORD = '<password>' OLD_PASSWORD = '<password>', CHECK_POLICY = OFF", anon);
        Assert.Contains("PASSWORD = '<password>' UNLOCK", anon);
        Assert.Contains("CREATE USER usr_002 WITH PASSWORD = '<password>', DEFAULT_SCHEMA = dbo", anon);
    }

    [Fact]
    public void Chiavi_certificati_e_credenziali()
    {
        var anon = Anon("""
            CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'm4ster!';
            CREATE CERTIFICATE Cert1 ENCRYPTION BY PASSWORD = 'c3rt!' WITH SUBJECT = 'x';
            CREATE SYMMETRIC KEY Chiave1 WITH ALGORITHM = AES_256 ENCRYPTION BY PASSWORD = 'k3y!';
            OPEN SYMMETRIC KEY Chiave1 DECRYPTION BY PASSWORD = 'k3y!';
            CREATE CREDENTIAL Cred1 WITH IDENTITY = 'ACMEDOM\svc', SECRET = 's3cret!';
            CREATE CREDENTIAL Cred2 WITH IDENTITY = 'SHARED ACCESS SIGNATURE', SECRET = 'sas-token';
            """);

        foreach (var segreto in new[] { "m4ster", "c3rt", "k3y", "s3cret", "sas-token" })
            Assert.DoesNotContain(segreto, anon);
        Assert.Equal(6, Occorrenze(anon, "'<password>'")); // chiave master, certificato, chiave simmetrica x2, due credenziali
        Assert.Contains("IDENTITY = 'dom_01\\usr_001'", anon);
        Assert.Contains("IDENTITY = 'SHARED ACCESS SIGNATURE'", anon);
    }

    [Fact]
    public void Password_e_sid_spariscono_anche_se_il_profilo_lascia_tutto_in_chiaro()
    {
        var profilo = NuovoProfilo();
        foreach (var ruolo in Enum.GetValues<Ruolo>()) profilo.Matrice[ruolo] = Trattamento.InChiaro;

        var anon = Anon("CREATE LOGIN svc WITH PASSWORD = 'segreta', SID = 0x1A2B3C4D5E6F708192A3B4C5D6E7F801;", profilo: profilo);

        Assert.Equal("CREATE LOGIN svc WITH PASSWORD = '<password>', SID = 0x00000000000000000000000000000000;", anon);
    }

    [Fact]
    public void Il_risultato_e_ancora_t_sql_valido()
    {
        var anon = Anon(LeggiEsempio("sicurezza-acme.sql"));

        Assert.NotEmpty(Identificatori(anon)); // Identificatori() controlla anche che il testo sia valido
    }

    // --- esempio completo ---

    [Fact]
    public void Esempio_completo_nessun_nome_ne_segreto_resta()
    {
        var anon = Anon(LeggiEsempio("sicurezza-acme.sql"), profilo: NuovoProfilo("Acme"));

        foreach (var vietata in new[] { "ACMEDOM", "mario", "rossi", "svc_vendite", "ruolo_contabilita", "Vendite", "Ordini",
                     "S3gr3ta", "1A2B3C", "nuova-password", "vecchia-password", "usp_OrdiniCliente" })
            Assert.DoesNotContain(vietata, anon.Replace("--", ""), StringComparison.OrdinalIgnoreCase);

        // Restano i nomi di sistema.
        Assert.Contains("db_datareader", anon);
        Assert.Contains("[NT AUTHORITY\\SYSTEM]", anon);
        Assert.Contains("sysadmin", anon);
        Assert.Contains("TO public", anon);
    }

    [Fact]
    public void Esempio_completo_andata_e_ritorno_senza_i_segreti()
    {
        // Le password non tornano (sono state cancellate): si prova lo script senza password né SID.
        var sql = LeggiEsempio("sicurezza-acme.sql");
        var senzaSegreti = System.Text.RegularExpressions.Regex.Replace(
                sql, @"CREATE LOGIN svc_vendite WITH PASSWORD.*?CHECK_POLICY = OFF;",
                "CREATE LOGIN svc_vendite FROM WINDOWS WITH DEFAULT_DATABASE = [Vendite];",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Replace("ALTER LOGIN svc_vendite WITH PASSWORD = 'nuova-password' OLD_PASSWORD = 'vecchia-password';", "ALTER LOGIN svc_vendite DISABLE;");
        Assert.DoesNotContain("S3gr3ta", senzaSegreti);
        Assert.DoesNotContain("nuova-password", senzaSegreti);
        var mappa = new MappaCliente();

        var anon = Anon(senzaSegreti, mappa);

        Assert.Equal(senzaSegreti, RipristinaPerProva(anon, mappa));
    }

    private static int Occorrenze(string testo, string cosa) =>
        (testo.Length - testo.Replace(cosa, "").Length) / cosa.Length;
}
