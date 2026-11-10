using Anonimizzatore.Interfaccia.Servizi;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Interfaccia.Test;

/// <summary>Lo stato dell'app: profili su disco, anonimizza e ripristina. Solo script e nomi INVENTATI.</summary>
public sealed class StatoAppTest : IDisposable
{
    private sealed class SenzaCifratura : IProtezioneDati
    {
        public byte[] Proteggi(byte[] dati) => dati;
        public byte[] Sblocca(byte[] datiProtetti) => datiProtetti;
    }

    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "anonimizzatore-test-" + Guid.NewGuid().ToString("N"));

    private StatoApp Nuovo() => new(new OpzioniApp(_cartella, "test"), new SenzaCifratura());

    public void Dispose()
    {
        if (Directory.Exists(_cartella)) Directory.Delete(_cartella, true);
    }

    [Fact]
    public async Task Senza_profili_si_parte_vuoti()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();

        Assert.True(stato.Caricato);
        Assert.Empty(stato.Profili);
        Assert.Null(stato.Corrente);
    }

    [Fact]
    public async Task Un_profilo_creato_si_ritrova_al_riavvio_ed_e_quello_in_uso()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        Assert.Null(await stato.CreaProfiloAsync("Cliente di prova"));

        var riavviato = Nuovo();
        await riavviato.CaricaAsync();

        Assert.Equal("Cliente di prova", riavviato.Corrente?.Profilo.Nome);
    }

    [Fact]
    public async Task Il_nome_del_profilo_non_puo_essere_vuoto_ne_doppio()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Alfa");

        Assert.NotNull(await stato.CreaProfiloAsync("  "));
        Assert.NotNull(await stato.CreaProfiloAsync("ALFA")); // il confronto non distingue maiuscole e minuscole
        Assert.Single(stato.Profili);
    }

    [Fact]
    public async Task Anonimizza_poi_Ripristina_riporta_i_nomi_veri_e_la_mappa_resta_dopo_il_riavvio()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        stato.Script[0] = "SELECT Id FROM Vendite.dbo.Ordini;";

        await stato.AnonimizzaAsync();

        var testo = stato.Anonimizzato!.Testo.Testo;
        Assert.Contains("tbl_001", testo);
        Assert.DoesNotContain("Ordini", testo);
        Assert.True(stato.AnonimizzatoAggiornato);

        // Dopo un riavvio la risposta dell'IA si ripristina lo stesso: la mappa è stata salvata.
        var riavviato = Nuovo();
        await riavviato.CaricaAsync();
        riavviato.RispostaIa = "Aggiungi un indice su tbl_001.";
        await riavviato.RipristinaAsync();

        Assert.Equal("Aggiungi un indice su Ordini.", riavviato.Ripristinato!.Risultato.Testo);
        Assert.Empty(riavviato.Ripristinato.Risultato.Avvisi);
    }

    [Fact]
    public async Task Se_gli_script_cambiano_il_risultato_non_e_piu_aggiornato()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        stato.Script[0] = "SELECT 1;";
        await stato.AnonimizzaAsync();
        Assert.True(stato.AnonimizzatoAggiornato);

        stato.Script[0] = "SELECT 2;";

        Assert.False(stato.AnonimizzatoAggiornato);
    }

    [Fact]
    public async Task Gli_script_vuoti_non_si_anonimizzano()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        stato.AggiungiScript();
        stato.Script[1] = "SELECT 1;";

        await stato.AnonimizzaAsync();

        Assert.Single(stato.Anonimizzato!.Risultato.Script);
    }

    [Fact]
    public async Task Rigenera_nomi_svuota_la_mappa_e_toglie_i_risultati()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        stato.Script[0] = "SELECT Id FROM dbo.Ordini;";
        await stato.AnonimizzaAsync();
        Assert.NotEmpty(stato.Corrente!.Mappa.Voci);

        await stato.RigeneraNomiAsync(stato.Corrente.Profilo.Id);

        Assert.Empty(stato.Corrente.Mappa.Voci);
        Assert.Null(stato.Anonimizzato);
    }

    [Fact]
    public async Task Cambiare_le_regole_del_profilo_toglie_il_risultato_gia_mostrato()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        stato.Script[0] = "SELECT Id FROM dbo.Ordini;";
        await stato.AnonimizzaAsync();

        var corrente = stato.Corrente!;
        corrente.Profilo.Blacklist.Add("Acme");
        await stato.SalvaAsync(corrente);

        Assert.Null(stato.Anonimizzato);
        var riavviato = Nuovo();
        await riavviato.CaricaAsync();
        Assert.Contains("Acme", riavviato.Corrente!.Profilo.Blacklist);
    }

    [Fact]
    public async Task Eliminare_il_profilo_in_uso_passa_a_un_altro_e_cancella_il_file()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Alfa");
        await stato.CreaProfiloAsync("Beta");
        var beta = stato.Corrente!.Profilo.Id;

        await stato.EliminaAsync(beta);

        Assert.Equal("Alfa", stato.Corrente?.Profilo.Nome);
        var riavviato = Nuovo();
        await riavviato.CaricaAsync();
        Assert.Single(riavviato.Profili);
    }

    [Fact]
    public async Task Un_file_di_profilo_illeggibile_si_segnala_e_non_blocca_gli_altri()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Alfa");
        File.WriteAllText(Path.Combine(_cartella, "profili", Guid.NewGuid() + ArchivioProfili.Estensione), "questo non è un profilo");

        var riavviato = Nuovo();
        await riavviato.CaricaAsync();

        Assert.Single(riavviato.Profili);
        Assert.Single(riavviato.ProfiliIllegibili);
    }

    [Fact]
    public async Task La_matrice_per_tipo_cambia_il_risultato()
    {
        var stato = Nuovo();
        await stato.CaricaAsync();
        await stato.CreaProfiloAsync("Cliente di prova");
        var corrente = stato.Corrente!;
        corrente.Profilo.Matrice[Ruolo.Tabella] = Trattamento.InChiaro;
        await stato.SalvaAsync(corrente);
        stato.Script[0] = "SELECT Id FROM dbo.Ordini;";

        await stato.AnonimizzaAsync();

        Assert.Contains("Ordini", stato.Anonimizzato!.Risultato.Script[0].Testo);
    }
}
