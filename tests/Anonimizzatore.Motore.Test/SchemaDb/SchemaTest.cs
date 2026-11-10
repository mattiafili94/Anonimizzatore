using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.SchemaDb;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.SchemaDb;

public class SchemaTest
{
    private static SchemaDatabase Esempio() => new([
        new TabellaSchema("dbo", "Ordini",
            [new("IdOrdine", "int", false, true), new("CodiceAcme", "varchar(20)", false), new("Importo", "decimal(18,2)", false), new("Note", "nvarchar(max)")],
            new("PK_Ordini", ["IdOrdine"]), null, [new("IX_Ordini_Codice", true, ["CodiceAcme"])]),
        new TabellaSchema("dbo", "Righe",
            [new("IdRiga", "int", false), new("IdOrdine", "int", false), new("Quantita", "int", false)],
            new("PK_Righe", ["IdRiga"]),
            [new("FK_Righe_Ordini", ["IdOrdine"], "dbo", "Ordini", ["IdOrdine"])]),
        new TabellaSchema("dbo", "Clienti", [new("IdCliente", "int", false), new("Nome", "varchar(100)")], new("PK_Clienti", ["IdCliente"])),
        new TabellaSchema("dbo", "vOrdiniAperti", [new("IdOrdine", "int", false), new("Importo", "decimal(18,2)", false)], EVista: true),
    ]);

    // --- il motore usa lo schema ---

    [Fact]
    public void I_tipi_dello_schema_danno_il_nome_alle_colonne_anonimizzate()
    {
        var sql = "SELECT CodiceAcme FROM dbo.Ordini;";

        Assert.Equal("SELECT col_x_001 FROM dbo.tbl_001;", Anon(sql, profilo: NuovoProfilo("Acme")));

        var esito = AnonimizzatoreScript.Anonimizza([sql], NuovoProfilo("Acme"), new MappaCliente(), Esempio());
        Assert.Equal("SELECT col_vc20_001 FROM dbo.tbl_001;", esito.Script[0].Testo);
    }

    [Fact]
    public void Una_vista_dello_schema_e_una_vista_anche_senza_il_suo_create()
    {
        var sql = "SELECT * FROM dbo.vOrdiniAperti;";

        Assert.Equal("SELECT * FROM dbo.tbl_001;", Anon(sql));
        Assert.Equal("SELECT * FROM dbo.vw_001;",
            AnonimizzatoreScript.Anonimizza([sql], NuovoProfilo(), new MappaCliente(), Esempio()).Script[0].Testo);
    }

    [Fact]
    public void I_nomi_dello_schema_non_vengono_mai_copiati_dai_nomi_generati()
    {
        var schema = new SchemaDatabase([new TabellaSchema("dbo", "Ordini", [new("tbl_001", "int")])]);

        var esito = AnonimizzatoreScript.Anonimizza(["SELECT * FROM dbo.Ordini;"], NuovoProfilo(), new MappaCliente(), schema);

        Assert.Equal("SELECT * FROM dbo.tbl_002;", esito.Script[0].Testo);
    }

    [Fact]
    public void Un_create_nello_script_ha_la_precedenza_sullo_schema()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["CREATE VIEW dbo.vOrdiniAperti AS SELECT 1 AS Uno;\nGO\nSELECT * FROM dbo.vOrdiniAperti;"],
            NuovoProfilo(), new MappaCliente(), new SchemaDatabase([new TabellaSchema("dbo", "vOrdiniAperti", [], EVista: false)]));

        Assert.Equal("CREATE VIEW dbo.vw_001 AS SELECT 1 AS Uno;\nGO\nSELECT * FROM dbo.vw_001;", esito.Script[0].Testo);
    }

    // --- lo schema anonimizzato ---

    private static (string Schema, string Script, MappaCliente Mappa) Genera(string sql, SchemaDatabase? schema = null, Profilo? profilo = null)
    {
        schema ??= Esempio();
        profilo ??= NuovoProfilo("Acme");
        var mappa = new MappaCliente();
        var script = AnonimizzatoreScript.Anonimizza([sql], profilo, mappa, schema).Script[0].Testo!;
        return (SchemaAnonimizzato.Genera(schema, [script], profilo, mappa), script, mappa);
    }

    [Fact]
    public void Solo_le_tabelle_citate_con_gli_stessi_nomi_degli_script()
    {
        var (schema, script, _) = Genera("SELECT o.CodiceAcme FROM dbo.Ordini AS o;");

        Assert.Equal("SELECT o.col_vc20_001 FROM dbo.tbl_001 AS o;", script);
        Assert.Equal("""
            CREATE TABLE dbo.tbl_001
            (
                IdOrdine int IDENTITY NOT NULL,
                col_vc20_001 varchar(20) NOT NULL,
                Importo decimal(18,2) NOT NULL,
                Note nvarchar(max) NULL,
                CONSTRAINT PK_tbl_001 PRIMARY KEY (IdOrdine)
            );
            CREATE UNIQUE INDEX IX_tbl_001_Codice ON dbo.tbl_001 (col_vc20_001);
            """.Replace("\r\n", "\n"), schema.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Le_tabelle_a_cui_puntano_le_chiavi_esterne_vengono_aggiunte()
    {
        var (schema, script, _) = Genera("SELECT * FROM dbo.Righe;");

        Assert.Equal("SELECT * FROM dbo.tbl_001;", script);
        Assert.Contains("CREATE TABLE dbo.tbl_001", schema);                 // Righe
        Assert.Contains("CREATE TABLE dbo.tbl_002", schema);                 // Ordini, citata solo dalla chiave esterna
        Assert.Contains("ALTER TABLE dbo.tbl_001 ADD CONSTRAINT FK_tbl_001_tbl_002 FOREIGN KEY (IdOrdine) REFERENCES dbo.tbl_002 (IdOrdine);", schema);
        Assert.DoesNotContain("Clienti", schema);
    }

    [Fact]
    public void Il_nome_di_una_colonna_anonimizzata_e_lo_stesso_nello_schema_e_negli_script()
    {
        var (schema, script, _) = Genera("SELECT CodiceAcme, Importo FROM dbo.Ordini WHERE CodiceAcme = 'A1';");

        Assert.Equal(2, Regex(script, "col_vc20_001"));
        Assert.Contains("col_vc20_001 varchar(20) NOT NULL", schema);
        Assert.DoesNotContain("CodiceAcme", schema);
    }

    [Fact]
    public void Una_vista_e_un_commento_con_le_colonne()
    {
        var (schema, _, _) = Genera("SELECT * FROM dbo.vOrdiniAperti;");

        Assert.Equal("-- VISTA dbo.vw_001 (IdOrdine int, Importo decimal(18,2))", schema);
    }

    [Fact]
    public void Tabelle_in_chiaro_per_matrice_o_whitelist_si_citano_con_il_nome_vero()
    {
        var profilo = NuovoProfilo();
        profilo.Whitelist.Add("Ordini");
        profilo.Matrice[Ruolo.Schema] = Trattamento.InChiaro;

        var (schema, script, _) = Genera("SELECT * FROM dbo.Ordini;", profilo: profilo);

        Assert.Equal("SELECT * FROM dbo.Ordini;", script);
        Assert.StartsWith("CREATE TABLE dbo.Ordini", schema);
        Assert.Contains("CONSTRAINT PK_Ordini PRIMARY KEY (IdOrdine)", schema);
    }

    [Fact]
    public void Nessuna_tabella_citata_schema_vuoto()
    {
        var (schema, _, _) = Genera("SELECT 1;");

        Assert.Equal("", schema);
    }

    [Fact]
    public void Nomi_che_richiedono_le_parentesi_quadre()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Tabella] = Trattamento.InChiaro;
        profilo.Matrice[Ruolo.Schema] = Trattamento.InChiaro;
        var strano = new SchemaDatabase([new TabellaSchema("Mio Schema", "Order", [new("Mia colonna", "int"), new("Group", "int")])]);

        var (schema, _, _) = Genera("SELECT * FROM [Mio Schema].[Order];", strano, profilo);

        Assert.StartsWith("CREATE TABLE [Mio Schema].[Order]", schema);
        Assert.Contains("[Mia colonna] int NULL", schema);
        Assert.Contains("[Group] int NULL", schema);
    }

    [Fact]
    public void Lo_schema_anonimizzato_si_de_anonimizza_come_gli_script()
    {
        var (schema, _, mappa) = Genera("SELECT o.CodiceAcme FROM dbo.Ordini AS o JOIN dbo.Righe r ON r.IdOrdine = o.IdOrdine;");

        var ripristinato = new DeAnonimizzatore(mappa).DeAnonimizza(schema).Testo;

        Assert.Contains("CREATE TABLE dbo.Ordini", ripristinato);
        Assert.Contains("CodiceAcme varchar(20) NOT NULL", ripristinato);
        Assert.Contains("CONSTRAINT PK_Ordini PRIMARY KEY (IdOrdine)", ripristinato);
        Assert.Contains("ALTER TABLE dbo.Righe ADD CONSTRAINT FK_Righe_Ordini FOREIGN KEY (IdOrdine) REFERENCES dbo.Ordini (IdOrdine);", ripristinato);
        Assert.Contains("CREATE UNIQUE INDEX IX_Ordini_Codice ON dbo.Ordini (CodiceAcme);", ripristinato);
    }

    [Fact]
    public void Lo_schema_anonimizzato_e_t_sql_valido()
    {
        var (schema, _, _) = Genera("SELECT * FROM dbo.Righe; SELECT * FROM dbo.Ordini;");

        Assert.True(Motore.Lettura.LettoreScript.Leggi(schema).Valido, schema);
    }

    private static int Regex(string testo, string cosa) => System.Text.RegularExpressions.Regex.Matches(testo, System.Text.RegularExpressions.Regex.Escape(cosa)).Count;
}
