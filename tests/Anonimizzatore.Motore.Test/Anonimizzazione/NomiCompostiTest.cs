using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using static Anonimizzatore.Motore.Test.Anonimizzazione.Aiuti;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>Indici e constraint: si sostituiscono le parti che sono oggetti anonimizzati (<c>FK_Righe_Ordini</c> → <c>FK_tbl_001_tbl_002</c>).</summary>
public class NomiCompostiTest
{
    private const string Tabelle = "CREATE TABLE dbo.Ordini (Id int); CREATE TABLE dbo.Righe (Id int);\n";

    [Theory]
    [InlineData("PK_Ordini", "PK_tbl_001")]
    [InlineData("FK_Righe_Ordini", "FK_tbl_002_tbl_001")]
    [InlineData("IX_Ordini_Data", "IX_tbl_001_Data")]
    [InlineData("pk_ordini", "pk_tbl_001")]
    [InlineData("[FK_Righe_Ordini]", "[FK_tbl_002_tbl_001]")]
    [InlineData("PK_Generico", "PK_Generico")]
    [InlineData("IX_Ordinine", "IX_Ordinine")]       // "Ordinine" non è "Ordini"
    public void Parti_pulite_vengono_sostituite(string nomeOriginale, string atteso)
    {
        var anon = Anon($"{Tabelle}CREATE INDEX {nomeOriginale} ON dbo.Ordini (Id);");

        Assert.EndsWith($"CREATE INDEX {atteso} ON dbo.tbl_001 (Id);", anon);
    }

    [Theory]
    [InlineData("IXOrdini")]         // parte attaccata a "IX"
    [InlineData("IX_OrdiniExtra")]   // attaccata a "Extra"
    [InlineData("IX_Ordini2")]       // attaccata a una cifra
    public void Parti_attaccate_ad_altro_anonimizzano_il_nome_intero(string nomeOriginale)
    {
        var anon = Anon($"{Tabelle}CREATE INDEX {nomeOriginale} ON dbo.Ordini (Id);");

        Assert.EndsWith("CREATE INDEX ix_001 ON dbo.tbl_001 (Id);", anon);
    }

    [Fact]
    public void Ogni_tipo_di_constraint_con_le_sue_parti()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Righe
            (
                Id int NOT NULL CONSTRAINT PK_Righe PRIMARY KEY,
                IdOrdine int NOT NULL CONSTRAINT FK_Righe_Ordini REFERENCES dbo.Ordini (Id),
                Quantita int NOT NULL CONSTRAINT DF_Righe_Quantita DEFAULT (1),
                Codice varchar(10) CONSTRAINT UQ_Righe_Codice UNIQUE,
                CONSTRAINT CK_Righe_Quantita CHECK (Quantita > 0),
                INDEX IX_Righe_Codice (Codice)
            );
            """);

        Assert.Contains("CONSTRAINT PK_tbl_001 PRIMARY KEY", anon);
        Assert.Contains("CONSTRAINT FK_tbl_001_tbl_002 REFERENCES dbo.tbl_002 (Id)", anon);
        Assert.Contains("CONSTRAINT DF_tbl_001_Quantita DEFAULT (1)", anon);
        Assert.Contains("CONSTRAINT UQ_tbl_001_Codice UNIQUE", anon);
        Assert.Contains("CONSTRAINT CK_tbl_001_Quantita CHECK", anon);
        Assert.Contains("INDEX IX_tbl_001_Codice (Codice)", anon);
    }

    [Fact]
    public void Il_constraint_viene_prima_della_tabella_che_cita()
    {
        var anon = Anon("""
            CREATE INDEX IX_Righe_Riga ON dbo.Righe (Riga);
            GO
            CREATE TABLE dbo.Righe (Riga int);
            """);

        Assert.Contains("CREATE INDEX IX_tbl_001_Riga ON dbo.tbl_001 (Riga);", anon);
    }

    [Fact]
    public void L_oggetto_citato_puo_stare_in_un_altro_script()
    {
        var esito = AnonimizzatoreScript.Anonimizza(
            ["CREATE INDEX IX_Righe_Riga ON dbo.Righe (Riga);", "CREATE TABLE dbo.Righe (Riga int);"],
            NuovoProfilo(), new MappaCliente());

        Assert.Equal("CREATE INDEX IX_tbl_001_Riga ON dbo.tbl_001 (Riga);", esito.Script[0].Testo);
    }

    [Fact]
    public void Tabella_con_nome_composto_e_tabella_piu_corta_vince_la_piu_lunga()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Ordini (Id int);
            CREATE TABLE dbo.OrdiniAperti (Id int);
            CREATE INDEX IX_OrdiniAperti_Data ON dbo.OrdiniAperti (Id);
            """);

        Assert.Contains("CREATE INDEX IX_tbl_002_Data ON dbo.tbl_002 (Id);", anon);
    }

    [Fact]
    public void Un_indice_di_una_tabella_conosciuta_alla_mappa_da_prima()
    {
        var mappa = new MappaCliente();
        Anon("CREATE TABLE dbo.Ordini (Id int);", mappa);

        var anon = Anon("CREATE INDEX IX_Ordini_Id ON dbo.Ordini (Id);", mappa);

        Assert.Equal("CREATE INDEX IX_tbl_001_Id ON dbo.tbl_001 (Id);", anon);
    }

    [Fact]
    public void Parte_con_oggetto_in_chiaro_non_viene_toccata()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.Tabella] = Trattamento.InChiaro;

        var anon = Anon($"{Tabelle}CREATE INDEX IX_Ordini_Data ON dbo.Ordini (Id);", profilo: profilo);

        Assert.EndsWith("CREATE INDEX IX_Ordini_Data ON dbo.Ordini (Id);", anon);
    }

    [Fact]
    public void Parola_in_blacklist_nel_nome_anonimizza_il_nome_intero()
    {
        var anon = Anon("""
            CREATE TABLE dbo.T (Id int CONSTRAINT PK_Acme PRIMARY KEY, X int CONSTRAINT FK_Acme_T REFERENCES dbo.T (Id));
            CREATE INDEX IX_Acme_Data ON dbo.T (Id);
            """, profilo: NuovoProfilo("Acme"));

        Assert.Contains("CONSTRAINT pk_001 PRIMARY KEY", anon);
        Assert.Contains("CONSTRAINT fk_001 REFERENCES", anon);
        Assert.Contains("CREATE INDEX ix_001 ON", anon);
    }

    [Fact]
    public void La_matrice_puo_chiedere_di_anonimizzare_tutti_gli_indici_e_constraint()
    {
        var profilo = NuovoProfilo();
        profilo.Matrice[Ruolo.PrimaryKey] = Trattamento.Anonimizza;
        profilo.Matrice[Ruolo.Indice] = Trattamento.Anonimizza;

        var anon = Anon("""
            CREATE TABLE dbo.Ordini (Id int CONSTRAINT PK_Ordini PRIMARY KEY);
            CREATE INDEX IX_Ordini_Id ON dbo.Ordini (Id);
            """, profilo: profilo);

        Assert.Contains("CONSTRAINT pk_001 PRIMARY KEY", anon);
        Assert.Contains("CREATE INDEX ix_001 ON dbo.tbl_001", anon);
    }

    [Fact]
    public void Drop_di_constraint_e_indici_usa_gli_stessi_nomi_della_creazione()
    {
        var anon = Anon("""
            CREATE TABLE dbo.Righe (Id int, CONSTRAINT PK_Righe PRIMARY KEY (Id));
            ALTER TABLE dbo.Righe DROP CONSTRAINT PK_Righe;
            CREATE INDEX IX_Righe_Id ON dbo.Righe (Id);
            DROP INDEX IX_Righe_Id ON dbo.Righe;
            ALTER INDEX IX_Righe_Id ON dbo.Righe REBUILD;
            """);

        Assert.Contains("DROP CONSTRAINT PK_tbl_001;", anon);
        Assert.Contains("DROP INDEX IX_tbl_001_Id ON dbo.tbl_001;", anon);
        Assert.Contains("ALTER INDEX IX_tbl_001_Id ON dbo.tbl_001 REBUILD;", anon);
    }

    [Fact]
    public void Andata_e_ritorno_dei_nomi_composti_e_di_quelli_interi()
    {
        var sql = """
            CREATE TABLE dbo.Ordini (Id int CONSTRAINT PK_Ordini PRIMARY KEY, X int CONSTRAINT FK_Ordini_Acme REFERENCES dbo.Ordini (Id));
            CREATE INDEX IX_Ordini_Data ON dbo.Ordini (Id);
            CREATE INDEX IXOrdini ON dbo.Ordini (X);
            """;
        var mappa = new MappaCliente();

        var anon = Anon(sql, mappa, NuovoProfilo("Acme"));

        Assert.Equal(sql, RipristinaPerProva(anon, mappa));
    }
}
