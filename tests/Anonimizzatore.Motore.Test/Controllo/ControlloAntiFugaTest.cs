using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Controllo;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.SchemaDb;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Controllo;

public class ControlloAntiFugaTest
{
    /// <summary>Anonimizza con il motore e controlla il risultato.</summary>
    private static EsitoControllo Controlla(string sql, params string[] blacklist) => Controlla([sql], blacklist);

    private static EsitoControllo Controlla(string[] script, string[] blacklist)
    {
        var profilo = NuovoProfilo(blacklist);
        var mappa = new MappaCliente();
        var risultato = AnonimizzatoreScript.Anonimizza(script, profilo, mappa);
        return ControlloAntiFuga.Verifica(script, risultato, mappa, profilo);
    }

    /// <summary>Controlla un testo "anonimizzato" scritto a mano, con una mappa che conosce già alcune tabelle e colonne.</summary>
    private static EsitoControllo ControllaTesto(string originale, string anonimizzato, params string[] blacklist)
    {
        var profilo = NuovoProfilo(blacklist);
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Database, "Vendite");
        mappa.Ottieni(Ruolo.Login, "ACMEDOM\\mario".Split('\\')[1]);
        mappa.Ottieni(Ruolo.Colonna, "CodiceAcme");
        return ControlloAntiFuga.Verifica([originale], new RisultatoAnonimizzazione([new ScriptAnonimizzato(anonimizzato, [])]), mappa, profilo);
    }

    // --- script puliti ---

    [Theory]
    [InlineData("vendite-ordini.sql")]
    [InlineData("ordini-righe-acme.sql")]
    [InlineData("sicurezza-acme.sql")]
    [InlineData("sql-dinamico-acme.sql")]
    [InlineData("sqlcmd-acme.sql")]
    public void Gli_esempi_anonimizzati_sono_puliti(string esempio)
    {
        var esito = Controlla(LeggiEsempio(esempio), "Acme");

        Assert.True(esito.Pulito, string.Join("\n", esito.Fughe.Select(f => f.Messaggio)));
        Assert.False(esito.RichiedeConferma);
    }

    [Fact]
    public void Nell_esempio_di_backup_restano_i_due_testi_liberi_e_il_controllo_li_trova()
    {
        var esito = Controlla(LeggiEsempio("backup-restore-acme.sql"), "Acme");

        Assert.True(esito.RichiedeConferma);
        Assert.Equal([4, 31], esito.Fughe.Where(f => f.Tipo == TipoFuga.NomeOriginale && f.Gravita == GravitaFuga.Bloccante).Select(f => f.Riga));
        Assert.All(esito.Fughe, f => Assert.Equal(0, f.Script));
    }

    // --- nomi originali ---

    [Fact]
    public void Nome_originale_dentro_una_stringa_blocca_con_la_riga()
    {
        var esito = Controlla("SELECT * FROM dbo.Ordini;\nPRINT 'Elaborati gli Ordini di oggi';");

        var fuga = Assert.Single(esito.Fughe);
        Assert.Equal(TipoFuga.NomeOriginale, fuga.Tipo);
        Assert.Equal(GravitaFuga.Bloccante, fuga.Gravita);
        Assert.Equal("Ordini", fuga.Valore);
        Assert.Equal(2, fuga.Riga);
        Assert.True(esito.RichiedeConferma);
    }

    [Fact]
    public void La_riga_dentro_una_stringa_su_piu_righe_e_quella_giusta()
    {
        var esito = ControllaTesto("x", "SELECT 'prima riga\nseconda riga\nterza con Ordini dentro';");

        Assert.Equal(3, Assert.Single(esito.Fughe).Riga);
    }

    [Fact]
    public void Nome_originale_come_identificatore_e_solo_un_avviso_se_lo_script_era_valido()
    {
        // Un alias di colonna con lo stesso nome di una tabella anonimizzata resta in chiaro per scelta.
        var esito = Controlla("SELECT COUNT(*) AS Ordini FROM dbo.Ordini;");

        var fuga = Assert.Single(esito.Fughe);
        Assert.Equal(GravitaFuga.Avviso, fuga.Gravita);
        Assert.Equal("Ordini", fuga.Valore);
        Assert.False(esito.RichiedeConferma);
    }

    [Fact]
    public void Nome_originale_come_identificatore_blocca_se_lo_script_era_in_modalita_di_riserva()
    {
        var esito = ControllaTesto("SELECT * FROM WHERE (", "SELECT Ordini FROM WHERE (");

        Assert.Equal(GravitaFuga.Bloccante, Assert.Single(esito.Fughe).Gravita);
    }

    [Fact]
    public void Nome_originale_tra_parentesi_quadre_e_senza_maiuscole()
    {
        var esito = ControllaTesto("x", "SELECT '[ordini]', 'vendite';");

        Assert.Equal(["ordini", "vendite"], esito.Fughe.Select(f => f.Valore));
    }

    [Fact]
    public void Parole_piu_lunghe_non_sono_nomi_originali()
    {
        var esito = ControllaTesto("x", "SELECT 'OrdiniArchivio', 'Ordinine', 'xOrdini', 'Ordini2';");

        Assert.True(esito.Pulito);
    }

    [Fact]
    public void I_nomi_anonimi_non_si_segnalano()
    {
        var esito = ControllaTesto("x", "SELECT col_vc20_001, @v_int_002 FROM dbo.tbl_001 AS al_001; EXEC [dom_01\\usr_001]; SELECT 'srv_01', 'mail_001@example.invalid';", "col", "usr", "tbl");

        Assert.True(esito.Pulito, string.Join("\n", esito.Fughe.Select(f => f.Messaggio)));
    }

    // --- blacklist ---

    [Fact]
    public void Parola_della_blacklist_in_una_stringa_o_in_un_identificatore()
    {
        var esito = ControllaTesto("x", "SELECT CodiceAcme, 'Fattura per ACME', N'x_acme_y' FROM dbo.Fatture;", "Acme");

        Assert.Equal(["CodiceAcme", "ACME", "x_acme_y"], esito.Fughe.Where(f => f.Tipo == TipoFuga.ParolaBlacklist).Select(f => f.Valore));
        Assert.All(esito.Fughe.Where(f => f.Tipo == TipoFuga.ParolaBlacklist), f => Assert.Equal(GravitaFuga.Bloccante, f.Gravita));
    }

    [Fact]
    public void Voce_della_blacklist_di_piu_parole_anche_separata()
    {
        var esito = ControllaTesto("x", "SELECT 'Cliente Acme Srl', 'acme_srl', 'AcmeSrl', 'Acme', 'Srl';", "Acme Srl");

        Assert.Equal(["Cliente Acme Srl".Substring(8), "acme_srl", "AcmeSrl"], esito.Fughe.Where(f => f.Tipo == TipoFuga.ParolaBlacklist).Select(f => f.Valore).Distinct());
    }

    [Fact]
    public void Variabile_con_parola_della_blacklist()
    {
        var esito = ControllaTesto("x", "SELECT @CodiceAcme;", "Acme");

        Assert.Equal("@CodiceAcme", Assert.Single(esito.Fughe).Valore);
    }

    // --- password ---

    [Fact]
    public void Una_password_rimasta_blocca_e_non_viene_mai_ripetuta()
    {
        var esito = ControllaTesto("x", @"CREATE LOGIN svc WITH PASSWORD = N'Segr3ta!';
            SELECT 'Server=a;Password=Pwd123;', 'sqlcmd -S x -P Altra456';");

        Assert.Equal(3, esito.Fughe.Count(f => f.Tipo == TipoFuga.Segreto));
        Assert.All(esito.Fughe, f =>
        {
            Assert.DoesNotContain("Segr3ta", f.Messaggio + f.Valore);
            Assert.DoesNotContain("Pwd123", f.Messaggio + f.Valore);
            Assert.DoesNotContain("Altra456", f.Messaggio + f.Valore);
        });
    }

    [Fact]
    public void I_segnaposto_e_le_variabili_non_sono_password_rimaste()
    {
        var esito = ControllaTesto("x", @"CREATE LOGIN a WITH PASSWORD = '<password>';
            ALTER LOGIN b WITH PASSWORD = 0x00;
            SELECT 'Password=<password>;', 'sqlcmd -P <password>';
            SELECT * FROM dbo.T WHERE Password = @pwd;");

        Assert.True(esito.Pulito, string.Join("\n", esito.Fughe.Select(f => f.Messaggio)));
    }

    // --- commenti, validità, dati personali ---

    [Fact]
    public void Un_commento_che_non_e_un_segnaposto_blocca()
    {
        var esito = ControllaTesto("x", "SELECT 1; -- per il cliente\nSELECT 2; /* c001 */ /* c0123 */");

        var fuga = Assert.Single(esito.Fughe);
        Assert.Equal(TipoFuga.CommentoNonSostituito, fuga.Tipo);
        Assert.Equal(1, fuga.Riga);
    }

    [Fact]
    public void Se_lo_script_era_valido_e_ora_non_lo_e_piu_blocca()
    {
        var esito = ControllaTesto("SELECT 1;", "SELECT FROM WHERE;");

        var fuga = Assert.Single(esito.Fughe);
        Assert.Equal(TipoFuga.ScriptNonValido, fuga.Tipo);
        Assert.Equal(GravitaFuga.Bloccante, fuga.Gravita);
    }

    [Fact]
    public void Se_lo_script_non_era_valido_non_si_pretende_che_lo_sia()
    {
        Assert.True(ControllaTesto("SELECT FROM WHERE;", "SELECT FROM WHERE;").Pulito);
    }

    [Fact]
    public void Le_righe_sqlcmd_non_contano_come_errori_di_sintassi()
    {
        var sql = ":setvar A 1\nSELECT 1;\n:connect srv_01 -U sa -P <password>\nGO 5\n";

        Assert.True(ControllaTesto(sql, sql).Pulito);
    }

    [Fact]
    public void Dati_personali_nelle_stringhe_sono_avvisi()
    {
        var esito = ControllaTesto("x", @"SELECT 'dba@acme.example', 'RSSMRA80A01H501U', 'IT60X0542811101000000123456',
            '4111 1111 1111 1111', '10.0.0.5', 'https://sito.example/area', 'mail_001@example.invalid', 'https://srv_01.dom_01.dom_02/x',
            '15.0.4000.1', 'IT60X0542811101000000123457', '1234 5678 9012 3456';");

        Assert.Equal(["un indirizzo email", "un codice fiscale", "un IBAN", "un numero di carta di credito", "un indirizzo IP", "un URL"],
            esito.Fughe.Select(f => f.Valore));
        Assert.All(esito.Fughe, f =>
        {
            Assert.Equal(TipoFuga.DatoPersonale, f.Tipo);
            Assert.Equal(GravitaFuga.Avviso, f.Gravita);
        });
        Assert.False(esito.RichiedeConferma);
    }

    [Fact]
    public void Numeri_e_identificatori_fuori_dalle_stringhe_non_sono_dati_personali()
    {
        Assert.True(ControllaTesto("x", "SELECT 4111111111111111, 10.0, RSSMRA80A01H501U FROM dbo.T;").Pulito);
    }

    // --- sqlcmd, schema, ordine ---

    [Fact]
    public void Una_riga_sqlcmd_con_un_nome_originale_e_segnalata_con_la_riga_dello_script()
    {
        var esito = ControllaTesto("x", "SELECT 1;\nGO\n:r D:\\Dati\\Ordini\\x.sql\nSELECT 2;");

        var fuga = Assert.Single(esito.Fughe);
        Assert.Equal(3, fuga.Riga);
        Assert.Contains("SQLCMD", fuga.Messaggio);
    }

    [Fact]
    public void Anche_lo_schema_allegato_si_controlla()
    {
        var profilo = NuovoProfilo();
        var mappa = new MappaCliente();
        var schema = new SchemaDatabase([new TabellaSchema("dbo", "Ordini", [new("Id", "int", false)])]);
        var risultato = AnonimizzatoreScript.Anonimizza(["SELECT * FROM dbo.Ordini;"], profilo, mappa, schema);
        var testoSchema = SchemaAnonimizzato.Genera(schema, [risultato.Script[0].Testo!], profilo, mappa);

        Assert.True(ControlloAntiFuga.Verifica(["SELECT * FROM dbo.Ordini;"], risultato, mappa, profilo, testoSchema).Pulito);

        var sporco = ControlloAntiFuga.Verifica(["SELECT * FROM dbo.Ordini;"], risultato, mappa, profilo, testoSchema + "\n-- tabella Ordini del cliente");
        Assert.Contains(sporco.Fughe, f => f.Script == -1 && f.Tipo == TipoFuga.CommentoNonSostituito);
        Assert.Contains(sporco.Fughe, f => f.Script == -1 && f.Tipo == TipoFuga.NomeOriginale);
    }

    [Fact]
    public void Piu_script_ordinati_per_script_e_per_riga_senza_doppioni()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        var risultato = new RisultatoAnonimizzazione([
            new ScriptAnonimizzato("SELECT 'Ordini', 'Ordini';\nSELECT 'ordini';", []),
            new ScriptAnonimizzato(null, [new Avviso("illeggibile")]),
            new ScriptAnonimizzato("SELECT 'Ordini';", []),
        ]);

        var esito = ControlloAntiFuga.Verifica(["a", "b", "c"], risultato, mappa, NuovoProfilo());

        Assert.Equal([(0, 1), (0, 2), (2, 1)], esito.Fughe.Select(f => (f.Script, f.Riga)));
    }

    [Fact]
    public void Numero_di_script_diverso_e_un_errore()
    {
        var risultato = new RisultatoAnonimizzazione([new ScriptAnonimizzato("SELECT 1;", [])]);

        Assert.Throws<ArgumentException>(() => ControlloAntiFuga.Verifica(["a", "b"], risultato, new MappaCliente(), NuovoProfilo()));
    }

    [Fact]
    public void Un_testo_senza_niente_da_controllare_e_pulito()
    {
        var esito = Controlla("SELECT 1;");

        Assert.True(esito.Pulito);
        Assert.Empty(esito.Fughe);
    }
}
