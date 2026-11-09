using System.Text;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Motore.Test.Profili;

/// <summary>Cifratura finta per i test: inverte i byte, così il file non è leggibile ma il test non dipende da Windows.</summary>
internal sealed class ProtezioneFinta : IProtezioneDati
{
    public byte[] Proteggi(byte[] dati) => dati.Select(b => (byte)~b).ToArray();
    public byte[] Sblocca(byte[] datiProtetti) => datiProtetti.Select(b => (byte)~b).ToArray();
}

public sealed class ArchivioProfiliTest : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "AnonimizzatoreTest_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cartella)) Directory.Delete(_cartella, recursive: true);
    }

    private static (Profilo, MappaCliente) Esempio()
    {
        var profilo = new Profilo { Nome = "Cliente Inventato Srl", Blacklist = ["Inventato"], Whitelist = ["Ordini"] };
        profilo.Matrice[Ruolo.Schema] = Trattamento.InChiaro;
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "AnagraficaInventato");
        mappa.Ottieni(Ruolo.Colonna, "CodiceInventato", "varchar(20)");
        return (profilo, mappa);
    }

    [Fact]
    public void Salva_e_ricarica_profilo_e_mappa()
    {
        var archivio = new ArchivioProfili(_cartella, new ProtezioneFinta());
        var (profilo, mappa) = Esempio();

        archivio.Salva(profilo, mappa);
        var caricato = archivio.Carica(profilo.Id);

        Assert.Equal("Cliente Inventato Srl", caricato.Profilo.Nome);
        Assert.Equal(["Inventato"], caricato.Profilo.Blacklist);
        Assert.Equal(["Ordini"], caricato.Profilo.Whitelist);
        Assert.Equal(Trattamento.InChiaro, caricato.Profilo.TrattamentoDi(Ruolo.Schema));
        Assert.Equal(Trattamento.Anonimizza, caricato.Profilo.TrattamentoDi(Ruolo.Tabella));
        Assert.Equal("tbl_001", caricato.Mappa.Ottieni(Ruolo.Tabella, "AnagraficaInventato"));
        Assert.Equal("tbl_002", caricato.Mappa.Ottieni(Ruolo.Tabella, "Altra"));
    }

    [Fact]
    public void Il_file_non_contiene_nomi_in_chiaro_e_non_ha_il_nome_del_cliente()
    {
        var archivio = new ArchivioProfili(_cartella, new ProtezioneFinta());
        var (profilo, mappa) = Esempio();

        archivio.Salva(profilo, mappa);

        var file = Assert.Single(Directory.GetFiles(_cartella));
        Assert.Equal(profilo.Id + ArchivioProfili.Estensione, Path.GetFileName(file));
        Assert.DoesNotContain("Inventato", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
    }

    [Fact]
    public void Carica_tutti_e_elimina()
    {
        var archivio = new ArchivioProfili(_cartella, new ProtezioneFinta());
        archivio.Salva(new Profilo { Nome = "Beta" }, new MappaCliente());
        var alfa = new Profilo { Nome = "alfa" };
        archivio.Salva(alfa, new MappaCliente());

        Assert.Equal(["alfa", "Beta"], archivio.CaricaTutti().Select(p => p.Profilo.Nome));

        archivio.Elimina(alfa.Id);
        Assert.Equal(["Beta"], archivio.CaricaTutti().Select(p => p.Profilo.Nome));
    }

    [Fact]
    public void Cartella_inesistente_nessun_profilo() =>
        Assert.Empty(new ArchivioProfili(_cartella, new ProtezioneFinta()).CaricaTutti());

    [Fact]
    public void Matrice_di_default()
    {
        var profilo = new Profilo { Nome = "x" };

        Assert.Equal(Trattamento.Anonimizza, profilo.TrattamentoDi(Ruolo.Tabella));
        Assert.Equal(Trattamento.Anonimizza, profilo.TrattamentoDi(Ruolo.Login));
        Assert.Equal(Trattamento.InChiaro, profilo.TrattamentoDi(Ruolo.Colonna));
        Assert.Equal(Trattamento.InChiaro, profilo.TrattamentoDi(Ruolo.Variabile));
        Assert.Equal(Trattamento.InChiaro, profilo.TrattamentoDi(Ruolo.Indice));
    }

    [Fact]
    public void Dpapi_vera_su_windows()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI esiste solo su Windows

        var protezione = new ProtezioneDpapi();
        var dati = "Cliente Inventato"u8.ToArray();

        var cifrato = protezione.Proteggi(dati);

        Assert.NotEqual(dati, cifrato);
        Assert.Equal(dati, protezione.Sblocca(cifrato));

        var archivio = new ArchivioProfili(_cartella, protezione);
        var (profilo, mappa) = Esempio();
        archivio.Salva(profilo, mappa);
        Assert.Equal("Cliente Inventato Srl", archivio.Carica(profilo.Id).Profilo.Nome);
    }
}
