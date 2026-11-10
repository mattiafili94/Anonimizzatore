using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.SchemaDb;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Test;

/// <summary>Import dello schema: la query dell'app e la lettura del risultato incollato. Nomi INVENTATI.</summary>
public class ImportSchemaTest
{
    private static string Tsv(params string[][] righe) =>
        string.Join("\r\n", righe.Select(r => string.Join('\t', r)));

    private static readonly string[] Intestazione = ["Tipo", "Schema", "Oggetto", "Nome", "Posizione", "Dettaglio", "Extra1", "Extra2", "Extra3"];

    private static string Esempio() => Tsv(
        Intestazione,
        ["T", "dbo", "Ordini", "", "0", "", "", "", ""],
        ["T", "dbo", "Clienti", "", "0", "", "", "", ""],
        ["V", "dbo", "OrdiniAperti", "", "0", "", "", "", ""],
        ["C", "dbo", "Ordini", "Id", "1", "int", "NOTNULL", "IDENTITY", ""],
        ["C", "dbo", "Ordini", "IdCliente", "2", "int", "NOTNULL", "", ""],
        ["C", "dbo", "Ordini", "Totale", "3", "decimal(18,2)", "NULLABLE", "", ""],
        ["C", "dbo", "Clienti", "Id", "1", "int", "NOTNULL", "IDENTITY", ""],
        ["C", "dbo", "Clienti", "Nome", "2", "varchar(50)", "NULLABLE", "", ""],
        ["C", "dbo", "OrdiniAperti", "Id", "1", "int", "NOTNULL", "", ""],
        ["PK", "dbo", "Ordini", "PK_Ordini", "1", "Id", "UNIQUE", "", ""],
        ["IX", "dbo", "Ordini", "IX_Ordini_Cliente", "1", "IdCliente", "", "", ""],
        ["FK", "dbo", "Ordini", "FK_Ordini_Clienti", "1", "IdCliente", "dbo", "Clienti", "Id"]);

    [Fact]
    public void Legge_tabelle_viste_colonne_e_vincoli()
    {
        var r = ImportSchema.Leggi(Esempio());

        Assert.True(r.Riuscito);
        Assert.Empty(r.Avvisi);
        var schema = r.Schema!;
        Assert.Equal(3, schema.Tabelle.Count);
        var ordini = schema.Tabelle.Single(t => t.Nome == "Ordini");
        Assert.False(ordini.EVista);
        Assert.Equal(["Id", "IdCliente", "Totale"], ordini.Colonne.Select(c => c.Nome));
        Assert.True(ordini.Colonne[0].EIdentity);
        Assert.False(ordini.Colonne[0].AmmetteNull);
        Assert.Equal("decimal(18,2)", ordini.Colonne[2].Tipo);
        Assert.Equal(["Id"], ordini.ChiavePrimaria!.Colonne);
        Assert.Equal("PK_Ordini", ordini.ChiavePrimaria.Nome);
        Assert.Equal("IX_Ordini_Cliente", ordini.Indici!.Single().Nome);
        var fk = ordini.ChiaviEsterne!.Single();
        Assert.Equal("Clienti", fk.TabellaRiferita);
        Assert.Equal(["Id"], fk.ColonneRiferite);
        Assert.True(schema.Tabelle.Single(t => t.Nome == "OrdiniAperti").EVista);
    }

    [Fact]
    public void Funziona_anche_senza_intestazione_e_con_celle_NULL_di_SSMS()
    {
        var senza = Tsv(
            ["T", "dbo", "Ordini", "NULL", "0", "NULL", "NULL", "NULL", "NULL"],
            ["C", "dbo", "Ordini", "Id", "1", "int", "NOTNULL", "NULL", "NULL"]);

        var r = ImportSchema.Leggi(senza);

        Assert.True(r.Riuscito);
        Assert.Single(r.Schema!.Tabelle.Single().Colonne);
    }

    [Fact]
    public void Funziona_con_un_CSV_con_virgolette()
    {
        var csv = "Tipo,Schema,Oggetto,Nome,Posizione,Dettaglio,Extra1,Extra2,Extra3\n" +
                  "C,dbo,\"Ordini, vecchi\",Id,1,int,NOTNULL,,\n";

        var r = ImportSchema.Leggi(csv);

        Assert.True(r.Riuscito);
        Assert.Equal("Ordini, vecchi", r.Schema!.Tabelle.Single().Nome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("questo non e un risultato\naltro testo")]
    public void Un_testo_che_non_e_il_risultato_da_un_errore_chiaro(string testo)
    {
        var r = ImportSchema.Leggi(testo);

        Assert.False(r.Riuscito);
        Assert.False(string.IsNullOrWhiteSpace(r.Errore));
    }

    [Fact]
    public void Una_tabella_senza_colonne_e_le_righe_incomprensibili_si_segnalano()
    {
        var testo = Tsv(
            Intestazione,
            ["T", "dbo", "Vuota", "", "0", "", "", "", ""],
            ["T", "dbo", "Ordini", "", "0", "", "", "", ""],
            ["C", "dbo", "Ordini", "Id", "1", "int", "NOTNULL", "", ""],
            ["??", "x"]);

        var r = ImportSchema.Leggi(testo);

        Assert.True(r.Riuscito);
        Assert.Equal(["Ordini"], r.Schema!.Tabelle.Select(t => t.Nome));
        Assert.Equal(2, r.Avvisi.Count);
    }

    [Fact]
    public void La_query_dell_app_e_T_SQL_valido()
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        parser.Parse(new StringReader(ImportSchema.Query), out var errori);

        Assert.Empty(errori);
    }

    [Fact]
    public void Lo_schema_si_salva_con_il_profilo_e_si_ritrova()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "anonimizzatore-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archivio = new ArchivioProfili(cartella, new ProtezioneNulla());
            var profilo = new Profilo { Nome = "Prova" };
            archivio.Salva(profilo, new MappaCliente(), ImportSchema.Leggi(Esempio()).Schema);

            var caricato = archivio.Carica(profilo.Id);

            Assert.Equal(3, caricato.Schema!.Tabelle.Count);
            Assert.Equal("FK_Ordini_Clienti", caricato.Schema.Tabelle.Single(t => t.Nome == "Ordini").ChiaviEsterne!.Single().Nome);
        }
        finally
        {
            if (Directory.Exists(cartella)) Directory.Delete(cartella, true);
        }
    }

    [Fact]
    public void Un_profilo_senza_schema_si_carica_con_schema_nullo()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "anonimizzatore-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archivio = new ArchivioProfili(cartella, new ProtezioneNulla());
            var profilo = new Profilo { Nome = "Prova" };
            archivio.Salva(profilo, new MappaCliente());

            Assert.Null(archivio.Carica(profilo.Id).Schema);
        }
        finally
        {
            if (Directory.Exists(cartella)) Directory.Delete(cartella, true);
        }
    }

    private sealed class ProtezioneNulla : IProtezioneDati
    {
        public byte[] Proteggi(byte[] dati) => dati;
        public byte[] Sblocca(byte[] datiProtetti) => datiProtetti;
    }
}
