using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Mappa;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Deanonimizzazione;

/// <summary>Come un'IA scrive davvero le sue risposte, e il report che l'utente vede.</summary>
public class ReportRispostaIaTest
{
    // tbl_001 = Ordini, col_vc20_001 = CodiceAcme, usp_001 = uspChiudi, dom_01\usr_001 = ACMEDOM\mario
    private static MappaCliente MappaDiProva()
    {
        var mappa = new MappaCliente();
        Anon("""
            CREATE TABLE dbo.Ordini (Id int, CodiceAcme varchar(20)); -- tabella principale
            SELECT CodiceAcme FROM dbo.Ordini; -- codici
            EXEC dbo.uspChiudi;
            CREATE LOGIN [ACMEDOM\mario] FROM WINDOWS;
            """, mappa, NuovoProfilo("Acme"));
        return mappa;
    }

    private static RisultatoDeAnonimizzazione Ripristina(string risposta, MappaCliente? mappa = null) =>
        new DeAnonimizzatore(mappa ?? MappaDiProva()).DeAnonimizza(risposta);

    [Fact]
    public void Risposta_in_markdown_con_prosa_grassetto_apici_e_blocco_di_codice()
    {
        var risposta = """
            La tabella **tbl_001** ha la colonna `col_vc20_001`. Ecco la query corretta:

            ```sql
            SELECT TOP (10) t.col_vc20_001, COUNT(*) AS n
            FROM [dbo].[tbl_001] AS t
            GROUP BY t.col_vc20_001;
            EXEC dbo.usp_001;
            ```

            Nota: "TBL_001" è indicizzata (tbl_001), e le colonne di tbl_001's tabella vanno bene.
            """;

        var esito = Ripristina(risposta);

        Assert.Equal("""
            La tabella **Ordini** ha la colonna `CodiceAcme`. Ecco la query corretta:

            ```sql
            SELECT TOP (10) t.CodiceAcme, COUNT(*) AS n
            FROM [dbo].[Ordini] AS t
            GROUP BY t.CodiceAcme;
            EXEC dbo.uspChiudi;
            ```

            Nota: "Ordini" è indicizzata (Ordini), e le colonne di Ordini's tabella vanno bene.
            """, esito.Testo);
        Assert.Empty(esito.Avvisi);
    }

    [Fact]
    public void Nomi_composti_inventati_dall_IA_e_tipo_sbagliato_della_colonna()
    {
        var esito = Ripristina("CREATE TABLE tbl_001_archivio (col_int_001 int, IX_tbl_001_Data int);");

        Assert.Equal("CREATE TABLE Ordini_archivio (CodiceAcme int, IX_Ordini_Data int);", esito.Testo);
    }

    [Fact]
    public void Numeri_scritti_senza_gli_zeri_si_riconoscono()
    {
        var esito = Ripristina("SELECT * FROM tbl_1; EXEC usp_1; -- e anche TBL_01");

        Assert.Equal("SELECT * FROM Ordini; EXEC uspChiudi; -- e anche Ordini", esito.Testo);
        Assert.Empty(esito.Avvisi);
    }

    [Fact]
    public void Login_con_dominio_nella_risposta()
    {
        var esito = Ripristina("ALTER ROLE db_owner ADD MEMBER [dom_01\\usr_001];");

        Assert.Equal("ALTER ROLE db_owner ADD MEMBER [ACMEDOM\\mario];", esito.Testo);
    }

    // --- il report ---

    [Fact]
    public void Un_nome_anonimo_che_non_esiste_e_segnalato_con_la_riga()
    {
        var esito = Ripristina("SELECT *\nFROM tbl_001 a\nJOIN tbl_099 b ON a.Id = b.Id\nWHERE b.x IN (SELECT 1 FROM TBL_099);");

        Assert.Equal("SELECT *\nFROM Ordini a\nJOIN tbl_099 b ON a.Id = b.Id\nWHERE b.x IN (SELECT 1 FROM TBL_099);", esito.Testo);
        var avviso = Assert.Single(esito.Avvisi);
        Assert.Contains("'tbl_099'", avviso.Messaggio);
        Assert.Contains("(2 volte)", avviso.Messaggio);
        Assert.Equal(3, avviso.Riga);
    }

    [Fact]
    public void Nomi_di_infrastruttura_senza_corrispondenza()
    {
        var esito = Ripristina(@"Copia in D:\Backup\dir_099\file_099.bak e leggi \\srv_099\dir_099\x.bak");

        Assert.Contains(esito.Avvisi, a => a.Messaggio.Contains("'dir_099'"));
        Assert.Equal(1, esito.Avvisi.Count(a => a.Messaggio.Contains("'dir_099'"))); // una volta sola, anche se trovato due volte
    }

    [Fact]
    public void Commenti_che_l_IA_ha_tolto_o_spostato()
    {
        var mappa = MappaDiProva();    // /* c001 */ = "-- tabella principale", /* c002 */ = "-- codici"

        var senza = Ripristina("SELECT CodiceAcme FROM tbl_001; /* c001 */", mappa);
        Assert.Contains(senza.Avvisi, a => a.Messaggio.Contains("1 commento") && a.Messaggio.Contains("/* c002 */") && a.Messaggio.Contains("non sono tornati"));

        var spostato = Ripristina("SELECT 1 /* c001 */ UNION SELECT 2;\n/* c002 */", mappa);
        Assert.Contains(spostato.Avvisi, a => a.Messaggio.Contains("riga a sé") && a.Messaggio.Contains("/* c001 */"));

        var sconosciuto = Ripristina("/* c001 */ /* c002 */ /* c077 */", mappa);
        Assert.Contains(sconosciuto.Avvisi, a => a.Messaggio.Contains("non esistono nella mappa") && a.Messaggio.Contains("/* c077 */"));
    }

    [Fact]
    public void Una_risposta_senza_segnaposto_e_un_pezzo_di_codice_non_un_commento_perso()
    {
        var esito = Ripristina("SELECT CodiceAcme FROM tbl_001;");

        Assert.DoesNotContain(esito.Avvisi, a => a.Messaggio.Contains("commento") || a.Messaggio.Contains("commenti"));
    }

    [Fact]
    public void Molti_commenti_persi_sono_elencati_in_breve()
    {
        var mappa = new MappaCliente();
        Anon(string.Join("\n", Enumerable.Range(1, 12).Select(i => $"SELECT {i}; -- commento {i}")), mappa);

        var esito = new DeAnonimizzatore(mappa).DeAnonimizza("SELECT 1; /* c001 */");

        var avviso = Assert.Single(esito.Avvisi);
        Assert.Contains("11 commenti", avviso.Messaggio);   // 12 nella mappa, 1 tornato
        Assert.Contains("e altri 3", avviso.Messaggio);     // se ne elencano 8
    }

    [Fact]
    public void Password_e_sid_azzerati_sono_da_riempire_a_mano()
    {
        var esito = Ripristina("CREATE LOGIN svc WITH PASSWORD = '<password>',\n SID = 0x00000000000000000000000000000000;\nALTER LOGIN x WITH PASSWORD = '<password>';");

        var password = Assert.Single(esito.Avvisi, a => a.Messaggio.Contains("<password>"));
        Assert.Contains("2 segnaposto", password.Messaggio);
        Assert.Equal(1, password.Riga);
        var sid = Assert.Single(esito.Avvisi, a => a.Messaggio.Contains("SID"));
        Assert.Equal(2, sid.Riga);
    }

    [Fact]
    public void Se_e_tornato_tutto_il_report_e_vuoto()
    {
        var mappa = MappaDiProva();

        var esito = Ripristina("SELECT col_vc20_001 FROM tbl_001;\n/* c001 */\n/* c002 */", mappa);

        Assert.Empty(esito.Avvisi);
        Assert.Equal(2, esito.Sostituzioni);
    }

    [Fact]
    public void Testo_senza_nomi_anonimi_resta_uguale_e_senza_avvisi()
    {
        var testo = "SELECT 1 AS Uno, N'ciao' AS Saluto; -- niente da fare";

        var esito = Ripristina(testo);

        Assert.Equal(testo, esito.Testo);
        Assert.Empty(esito.Avvisi);
        Assert.Equal(0, esito.Sostituzioni);
    }
}
