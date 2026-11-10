using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Legenda;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.SchemaDb;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Legenda;

public class LegendaTest
{
    // --- i codici dei tipi, nei due sensi ---

    [Theory]
    [InlineData("varchar(50)", "vc50", "varchar(50)")]
    [InlineData("NVARCHAR(MAX)", "nvcmax", "nvarchar(max)")]
    [InlineData("decimal(18,2)", "dec18_2", "decimal(18,2)")]
    [InlineData("numeric( 10 , 0 )", "num10_0", "numeric(10,0)")]
    [InlineData("char(10)", "ch10", "char(10)")]
    [InlineData("int", "int", "int")]
    [InlineData("bigint", "bint", "bigint")]
    [InlineData("datetime", "dt", "datetime")]
    [InlineData("datetime2(0)", "dt2", "datetime2")]
    [InlineData("bit", "bit", "bit")]
    [InlineData("uniqueidentifier", "guid", "uniqueidentifier")]
    [InlineData("rowversion", "rv", "rowversion")]
    public void Il_codice_del_tipo_si_traduce_anche_al_contrario(string tipo, string codice, string descrizione)
    {
        Assert.Equal(codice, CodiceTipo.Da(tipo));
        Assert.Equal(descrizione, CodiceTipo.Descrivi(codice));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("")]
    [InlineData("zzz9")]
    public void Codici_sconosciuti_non_hanno_descrizione(string codice) => Assert.Null(CodiceTipo.Descrivi(codice));

    // --- la legenda spiega solo ciò che c'è ---

    [Fact]
    public void Solo_le_convenzioni_usate_nel_testo()
    {
        var legenda = GeneratoreLegenda.Genera("SELECT * FROM dbo.tbl_001 JOIN dbo.vw_002 ON 1 = 1");

        Assert.Contains("tbl_001: tabella", legenda);
        Assert.Contains("vw_001: vista", legenda);
        Assert.DoesNotContain("usp_", legenda);
        Assert.DoesNotContain("col_<tipo>", legenda);
        Assert.DoesNotContain("/* c001 */", legenda);
        Assert.DoesNotContain("<password>", legenda);
    }

    [Fact]
    public void Testo_senza_nomi_convenzionali_ha_solo_le_istruzioni()
    {
        var legenda = GeneratoreLegenda.Genera("SELECT 1;");

        Assert.Contains("usa questi nomi esattamente come sono", legenda);
        Assert.DoesNotContain("--   ", legenda);   // nessuna voce dell'elenco
    }

    [Fact]
    public void Ogni_riga_della_legenda_e_un_commento()
    {
        var legenda = GeneratoreLegenda.Genera(
            "tbl_001 vw_001 usp_001 fn_001 trg_001 sch_01 db_01 srv_01 dom_01 usr_001 rol_001 job_001 ix_001 pk_001 al_001 dir_001 file_001 ip_001 " +
            "mail_001@example.invalid #tmp_001 col_vc50_001 @v_int_001 @p_vc20_002 /* c001 */ <password> 0x00000000000000000000000000000000");

        Assert.All(legenda.Split('\n').Select(r => r.TrimEnd('\r')), riga => Assert.StartsWith("--", riga));
        foreach (var voce in new[] { "tbl_001: tabella", "vw_001: vista", "usp_001: stored procedure", "fn_001: funzione", "trg_001: trigger",
                     "sch_01: schema", "db_01: database", "srv_01: server", "dom_01: dominio", "usr_001: login o utente", "rol_001: ruolo",
                     "job_001: job di SQL Agent", "ix_001: indice", "pk_001, fk_001", "al_001: alias", "dir_001: cartella", "file_001: file",
                     "ip_001: indirizzo IP", "mail_001@example.invalid: indirizzo email", "#tmp_001: tabella temporanea", "/* c001 */",
                     "<password>: una password", "SID, rimosso" })
            Assert.Contains(voce, legenda);
    }

    [Fact]
    public void I_tipi_usati_sono_spiegati_in_ordine()
    {
        var legenda = GeneratoreLegenda.Genera("SELECT col_vc50_001, col_dec18_2_002, col_x_003, COL_VC50_004 FROM dbo.tbl_001");

        Assert.Contains("col_<tipo>_NNN: colonna con il nome nascosto; <tipo> è il suo tipo di dato (dec18_2 = decimal(18,2), vc50 = varchar(50), x = tipo non noto)", legenda);
    }

    [Fact]
    public void Variabili_e_parametri()
    {
        var legenda = GeneratoreLegenda.Genera("DECLARE @v_int_001 int; EXEC dbo.usp_001 @p_vc20_002 = 'a'");

        Assert.Contains("@v_<tipo>_NNN: variabile; @p_<tipo>_NNN: parametro", legenda);
        Assert.Contains("int = int", legenda);
        Assert.Contains("vc20 = varchar(20)", legenda);
    }

    [Fact]
    public void Nomi_composti_di_constraint_e_indici()
    {
        Assert.Contains("FK_tbl_001_tbl_002", GeneratoreLegenda.Genera("CONSTRAINT FK_tbl_002_tbl_001 FOREIGN KEY"));
        Assert.DoesNotContain("FK_tbl_001_tbl_002", GeneratoreLegenda.Genera("CONSTRAINT FK_Generico FOREIGN KEY"));
    }

    // --- testo unico ---

    private static (RisultatoAnonimizzazione Risultato, MappaCliente Mappa) Anonimizza(params string[] script)
    {
        var mappa = new MappaCliente();
        return (AnonimizzatoreScript.Anonimizza(script, NuovoProfilo("Acme"), mappa), mappa);
    }

    [Fact]
    public void Un_solo_script_non_ha_intestazione()
    {
        var (r, mappa) = Anonimizza("SELECT * FROM dbo.Ordini;");

        var testo = TestoPerIA.Componi(r, mappa).Testo;

        Assert.DoesNotContain("-- Script", testo);
        Assert.StartsWith("-- LEGENDA", testo);
        Assert.EndsWith("SELECT * FROM dbo.tbl_001;\r\n".Replace("\r\n", Environment.NewLine), testo);
    }

    [Fact]
    public void Piu_script_separati_da_intestazioni()
    {
        var (r, mappa) = Anonimizza("SELECT * FROM dbo.Ordini;", "SELECT * FROM dbo.Clienti;\nGO", "DROP TABLE dbo.Ordini;");

        var testo = TestoPerIA.Componi(r, mappa).Testo.Replace("\r\n", "\n");

        var inizio = testo.IndexOf("-- Script 1", StringComparison.Ordinal);
        Assert.True(inizio > testo.IndexOf("-- LEGENDA", StringComparison.Ordinal));
        Assert.Equal("""
            -- Script 1
            SELECT * FROM dbo.tbl_001;

            -- Script 2
            SELECT * FROM dbo.tbl_002;
            GO

            -- Script 3
            DROP TABLE dbo.tbl_001;

            """.Replace("\r\n", "\n"), testo[inizio..]);
    }

    [Fact]
    public void Uno_script_non_anonimizzabile_resta_fuori_e_non_cambia_la_numerazione()
    {
        var (r, mappa) = Anonimizza("SELECT 1;", "SELECT 2;", "SELECT 3;");
        var risultato = new RisultatoAnonimizzazione([r.Script[0], new ScriptAnonimizzato(null, [new Avviso("illeggibile")]), r.Script[2]]);

        var unico = TestoPerIA.Componi(risultato, mappa);

        Assert.Equal([1], unico.ScriptEsclusi);
        Assert.Contains("-- Script 1", unico.Testo);
        Assert.DoesNotContain("-- Script 2", unico.Testo);
        Assert.Contains("-- Script 3", unico.Testo);
    }

    [Fact]
    public void La_legenda_riguarda_solo_cio_che_e_nel_testo_finale()
    {
        var (r, mappa) = Anonimizza("EXEC dbo.uspChiudiOrdini @CodiceAcme = 'x' -- nota\n;");

        var testo = TestoPerIA.Componi(r, mappa).Testo;

        Assert.Contains("usp_001: stored procedure", testo);
        Assert.Contains("/* c001 */", testo);
        Assert.DoesNotContain("tbl_001: tabella", testo);
    }

    [Fact]
    public void Il_testo_finale_non_contiene_nomi_veri_ne_segreti()
    {
        var (r, mappa) = Anonimizza(
            "CREATE LOGIN [ACMEDOM\\mario] WITH PASSWORD = 'Segr3ta'; -- per Acme\nSELECT * FROM Vendite.dbo.OrdiniAcme;");

        var testo = TestoPerIA.Componi(r, mappa).Testo;

        foreach (var vietata in new[] { "ACMEDOM", "mario", "Segr3ta", "Acme", "Vendite" })
            Assert.DoesNotContain(vietata, testo.Replace("-- LEGENDA", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Con_lo_schema_serve_il_profilo()
    {
        var (r, mappa) = Anonimizza("SELECT 1;");

        Assert.Throws<ArgumentNullException>(() => TestoPerIA.Componi(r, mappa, new SchemaDatabase([])));
    }

    [Fact]
    public void Lo_schema_viene_dopo_la_legenda_e_prima_degli_script()
    {
        var schema = new SchemaDatabase([new TabellaSchema("dbo", "Ordini", [new("Id", "int", false)])]);
        var profilo = NuovoProfilo();
        var mappa = new MappaCliente();
        var r = AnonimizzatoreScript.Anonimizza(["SELECT * FROM dbo.Ordini;"], profilo, mappa, schema);

        var testo = TestoPerIA.Componi(r, mappa, schema, profilo).Testo;

        var iLegenda = testo.IndexOf("-- LEGENDA", StringComparison.Ordinal);
        var iSchema = testo.IndexOf("-- SCHEMA", StringComparison.Ordinal);
        var iCreate = testo.IndexOf("CREATE TABLE dbo.tbl_001", StringComparison.Ordinal);
        var iScript = testo.IndexOf("SELECT * FROM dbo.tbl_001;", StringComparison.Ordinal);
        Assert.True(iLegenda < iSchema && iSchema < iCreate && iCreate < iScript, testo);
    }

    [Fact]
    public void Senza_schema_niente_sezione_schema()
    {
        var (r, mappa) = Anonimizza("SELECT * FROM dbo.Ordini;");

        Assert.DoesNotContain("-- SCHEMA", TestoPerIA.Componi(r, mappa).Testo);
    }
}
