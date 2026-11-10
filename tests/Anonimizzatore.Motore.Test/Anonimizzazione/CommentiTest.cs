using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.Test.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Livello 5: i commenti diventano segnaposto <c>/* c001 */</c> e il testo originale resta nella mappa.</summary>
public sealed class CommentiTest : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "AnonimizzatoreTestCommenti_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cartella)) Directory.Delete(_cartella, recursive: true);
    }

    [Fact]
    public void I_commenti_diventano_segnaposto_e_il_loro_testo_sparisce_dallo_script()
    {
        var anon = Anon("""
            -- Calcolo mensile per Acme Srl, password: Segreta123
            SELECT 1; /* nota interna
                          su due righe */
            SELECT 2; -- fine
            """);

        Assert.Equal("""
            /* c001 */
            SELECT 1; /* c002 */
            SELECT 2; /* c003 */
            """, anon);
        Assert.DoesNotContain("Segreta123", anon);
        Assert.DoesNotContain("Acme", anon);
    }

    [Fact]
    public void Lo_stesso_testo_di_commento_riceve_sempre_lo_stesso_segnaposto()
    {
        var mappa = new MappaCliente();

        var primo = Anon("-- TODO\nSELECT 1; -- TODO\nSELECT 2; -- altro", mappa);
        var secondo = Anon("SELECT 3; -- altro\n-- TODO", mappa);

        Assert.Equal("/* c001 */\nSELECT 1; /* c001 */\nSELECT 2; /* c002 */", primo);
        Assert.Equal("SELECT 3; /* c002 */\n/* c001 */", secondo);
        Assert.Equal(2, mappa.Commenti.Commenti.Count);
    }

    [Fact]
    public void La_numerazione_prosegue_tra_script_diversi()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["SELECT 1; -- uno", "SELECT 2; -- due"], NuovoProfilo(), new MappaCliente());

        Assert.Equal("SELECT 1; /* c001 */", esito.Script[0].Testo);
        Assert.Equal("SELECT 2; /* c002 */", esito.Script[1].Testo);
    }

    [Fact]
    public void Commenti_e_nomi_insieme_andata_e_ritorno()
    {
        var sql = LeggiEsempio("vendite-ordini.sql");
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        Assert.Contains("/* c001 */", anon);
        Assert.DoesNotContain("Restituisce gli ordini", anon);
        Assert.DoesNotContain("filtro sulla data", anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void Un_commento_vicino_a_un_nome_non_lo_confonde()
    {
        var sql = "SELECT * FROM dbo.Ordini /* vecchia tabella Ordini */ WHERE 1 = 1; -- Ordini\n";
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        Assert.Equal("SELECT * FROM dbo.tbl_001 /* c001 */ WHERE 1 = 1; /* c002 */\n", anon);
        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void Il_testo_di_un_commento_che_somiglia_a_un_nome_anonimo_torna_uguale()
    {
        // Il commento originale dice "tbl_001": nel ripristino non deve essere riscritto come nome.
        var sql = "SELECT * FROM dbo.Ordini; -- vedi tbl_001 e col_x_001\n";
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa);

        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }

    [Fact]
    public void Il_deanonimizzatore_segnala_i_commenti_persi_dall_IA()
    {
        var mappa = new MappaCliente();
        Anon("SELECT 1; -- uno\nSELECT 2; -- due\n", mappa);

        var risposta = "Ecco la query:\n```sql\nSELECT 1; /* c001 */\nSELECT 2;\n```";
        var esito = new DeAnonimizzatore(mappa).DeAnonimizza(risposta);

        Assert.Contains("SELECT 1; -- uno\n", esito.Testo);
        Assert.NotNull(esito.Commenti);
        Assert.Equal([2], esito.Commenti.Persi);
        Assert.Equal(["/* c002 */"], esito.Commenti.PersiComeSegnaposto);
    }

    [Fact]
    public void I_commenti_si_ripristinano_anche_se_l_IA_cambia_gli_spazi_e_le_maiuscole()
    {
        var mappa = new MappaCliente();
        Anon("SELECT 1; -- uno\n", mappa);

        var esito = new DeAnonimizzatore(mappa).DeAnonimizza("SELECT 1; /*C001*/\n");

        Assert.Equal("SELECT 1; -- uno\n", esito.Testo);
        Assert.Empty(esito.Commenti!.Persi);
    }

    [Fact]
    public void I_commenti_restano_nel_file_cifrato_del_profilo()
    {
        var archivio = new ArchivioProfili(_cartella, new ProtezioneFinta());
        var profilo = NuovoProfilo();
        var mappa = new MappaCliente();
        var anon = Anon("SELECT * FROM dbo.Ordini; -- dati riservati di Acme\n", mappa, profilo);

        archivio.Salva(profilo, mappa);

        var file = Assert.Single(Directory.GetFiles(_cartella));
        Assert.DoesNotContain("riservati", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));

        var ricaricata = archivio.Carica(profilo.Id).Mappa;
        Assert.Equal("SELECT * FROM dbo.Ordini; -- dati riservati di Acme\n", RipristinaPerProva(anon, ricaricata));
        Assert.Equal("SELECT 1; /* c002 */", Anon("SELECT 1; -- nuovo", ricaricata)); // la numerazione prosegue
    }

    [Fact]
    public void Un_profilo_salvato_prima_della_tappa_6_si_carica_senza_commenti()
    {
        // DatiMappa senza il campo Commenti (come nei file vecchi) = nessun commento.
        var vecchi = new DatiMappa([], new Dictionary<string, int>());
        var mappa = MappaCliente.Da(vecchi);

        Assert.Empty(mappa.Commenti.Commenti);
        Assert.Equal("SELECT 1; /* c001 */", Anon("SELECT 1; -- x", mappa));
    }

    [Fact]
    public void Rigenera_nomi_azzera_anche_i_commenti()
    {
        var mappa = new MappaCliente();
        Anon("SELECT 1; -- uno\nSELECT 2; -- due", mappa);

        mappa.Rigenera();

        Assert.Equal("SELECT 3; /* c001 */", Anon("SELECT 3; -- tre", mappa));
    }

    [Fact]
    public void Anche_uno_script_con_errori_ha_i_commenti_sostituiti()
    {
        var mappa = new MappaCliente();
        var esito = AnonimizzatoreScript.Anonimizza(
            ["SELECT * FORM -- rotto", "SELECT 1; -- buono"], NuovoProfilo(), mappa);

        Assert.Equal("SELECT * FORM /* c001 */", esito.Script[0].Testo);
        Assert.Equal("SELECT 1; /* c002 */", esito.Script[1].Testo);
    }
}
