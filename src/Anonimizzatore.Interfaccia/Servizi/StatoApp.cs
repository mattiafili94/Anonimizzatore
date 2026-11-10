using System.Text.Json;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Controllo;
using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Legenda;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Interfaccia.Servizi;

/// <summary>Il risultato dell'ultimo "Anonimizza": tutto ciò che serve per mostrarlo, copiarlo e controllarlo.</summary>
public sealed record SessioneAnonimizzazione(
    IReadOnlyList<string> ScriptOriginali,
    RisultatoAnonimizzazione Risultato,
    TestoUnico Testo,
    EsitoControllo Controllo,
    Guid ProfiloId);

/// <summary>Il risultato dell'ultimo "Ripristina".</summary>
public sealed record SessioneRipristino(string Risposta, RisultatoDeAnonimizzazione Risultato, Guid ProfiloId);

/// <summary>
/// Lo stato dell'app: i profili (con le loro mappe), quello in uso e il lavoro in corso nelle due schermate, che resta anche
/// se si cambia pagina. Chiama il motore; non contiene regole di anonimizzazione.
/// </summary>
public sealed class StatoApp(OpzioniApp opzioni, IProtezioneDati protezione)
{
    private readonly ArchivioProfili _archivio = new(Path.Combine(opzioni.CartellaDati, "profili"), protezione);
    private readonly string _fileImpostazioni = Path.Combine(opzioni.CartellaDati, "impostazioni.json");
    private readonly SemaphoreSlim _una = new(1, 1);
    private List<ProfiloSalvato> _profili = [];
    private Guid? _correnteId;

    public event Action? Cambiato;

    public bool Caricato { get; private set; }
    public bool Occupato { get; private set; }
    public IReadOnlyList<ProfiloSalvato> Profili => _profili;

    /// <summary>File di profilo che non si sono potuti leggere (con il motivo): se ne parla all'utente, non si ignorano.</summary>
    public IReadOnlyList<string> ProfiliIllegibili { get; private set; } = [];

    public ProfiloSalvato? Corrente => _profili.FirstOrDefault(p => p.Profilo.Id == _correnteId);

    public string Versione => opzioni.Versione;
    public string CartellaDati => opzioni.CartellaDati;
    public bool Anteprima => opzioni.Anteprima;

    // --- lavoro in corso: Anonimizza ---

    /// <summary>Il contenuto degli editor degli script (sempre almeno uno).</summary>
    public List<string> Script { get; } = [""];

    public SessioneAnonimizzazione? Anonimizzato { get; private set; }

    /// <summary>Gli script non vuoti, nell'ordine: sono quelli che si anonimizzano.</summary>
    public IReadOnlyList<string> ScriptDaAnonimizzare => [.. Script.Where(s => !string.IsNullOrWhiteSpace(s))];

    /// <summary>Vero se il risultato mostrato corrisponde agli script e al profilo di adesso.</summary>
    public bool AnonimizzatoAggiornato =>
        Anonimizzato is { } a && a.ProfiloId == _correnteId && a.ScriptOriginali.SequenceEqual(ScriptDaAnonimizzare);

    // --- lavoro in corso: Ripristina ---

    public string RispostaIa { get; set; } = "";

    public SessioneRipristino? Ripristinato { get; private set; }

    public bool RipristinatoAggiornato =>
        Ripristinato is { } r && r.ProfiloId == _correnteId && r.Risposta == RispostaIa;

    // --- caricamento ---

    public async Task CaricaAsync()
    {
        await _una.WaitAsync();
        try
        {
            var (profili, errori) = await Task.Run(_archivio.CaricaTuttiTollerante);
            _profili = [.. profili];
            ProfiliIllegibili = errori;
            _correnteId = LeggiProfiloCorrente();
            if (Corrente is null) _correnteId = _profili.FirstOrDefault()?.Profilo.Id;
            Caricato = true;
        }
        finally
        {
            _una.Release();
        }
        Cambiato?.Invoke();
    }

    // --- profili ---

    /// <summary>Un nome valido per un profilo, o il motivo per cui non lo è.</summary>
    public string? ErroreNomeProfilo(string? nome, Guid? ignora = null)
    {
        nome = nome?.Trim();
        if (string.IsNullOrEmpty(nome)) return "Scrivi un nome per il profilo.";
        if (_profili.Any(p => p.Profilo.Id != ignora && NomeSql.Confronto.Equals(p.Profilo.Nome, nome)))
            return "Esiste già un profilo con questo nome.";
        return null;
    }

    public async Task<string?> CreaProfiloAsync(string nome)
    {
        if (ErroreNomeProfilo(nome) is { } errore) return errore;
        var profilo = new ProfiloSalvato(new Profilo { Nome = nome.Trim() }, new MappaCliente());
        await SalvaFileAsync(profilo);
        _profili = [.. _profili.Append(profilo).OrderBy(p => p.Profilo.Nome, NomeSql.Confronto)];
        await SelezionaAsync(profilo.Profilo.Id);
        return null;
    }

    public async Task SelezionaAsync(Guid id)
    {
        if (_profili.All(p => p.Profilo.Id != id)) return;
        _correnteId = id;
        await Task.Run(() => ScriviProfiloCorrente(id));
        Cambiato?.Invoke();
    }

    public async Task<string?> RinominaAsync(Guid id, string nome)
    {
        var profilo = _profili.FirstOrDefault(p => p.Profilo.Id == id);
        if (profilo is null) return "Profilo non trovato.";
        if (ErroreNomeProfilo(nome, id) is { } errore) return errore;
        profilo.Profilo.Nome = nome.Trim();
        await SalvaFileAsync(profilo);
        _profili = [.. _profili.OrderBy(p => p.Profilo.Nome, NomeSql.Confronto)];
        Cambiato?.Invoke();
        return null;
    }

    /// <summary>Salva un profilo cambiato (regole, liste). Il risultato già mostrato non vale più: era fatto con le regole di prima.</summary>
    public async Task SalvaAsync(ProfiloSalvato profilo)
    {
        await SalvaFileAsync(profilo);
        if (profilo.Profilo.Id == _correnteId) Anonimizzato = null;
        Cambiato?.Invoke();
    }

    public async Task EliminaAsync(Guid id)
    {
        await _una.WaitAsync();
        try
        {
            await Task.Run(() => _archivio.Elimina(id));
        }
        finally
        {
            _una.Release();
        }
        _profili = [.. _profili.Where(p => p.Profilo.Id != id)];
        if (_correnteId == id) _correnteId = _profili.FirstOrDefault()?.Profilo.Id;
        await Task.Run(() => ScriviProfiloCorrente(_correnteId));
        Cambiato?.Invoke();
    }

    /// <summary>
    /// "Rigenera nomi": svuota la mappa del profilo (nomi anonimi e commenti) e riparte da 1. Il risultato mostrato non vale più,
    /// perché i suoi nomi non si ripristinerebbero.
    /// </summary>
    public async Task RigeneraNomiAsync(Guid id)
    {
        var profilo = _profili.FirstOrDefault(p => p.Profilo.Id == id);
        if (profilo is null) return;
        profilo.Mappa.Rigenera();
        await SalvaFileAsync(profilo);
        if (id == _correnteId)
        {
            Anonimizzato = null;
            Ripristinato = null;
        }
        Cambiato?.Invoke();
    }

    // --- Anonimizza ---

    public void AggiungiScript()
    {
        Script.Add("");
        Cambiato?.Invoke();
    }

    public void RimuoviScript(int indice)
    {
        if (Script.Count <= 1) Script[0] = "";
        else Script.RemoveAt(indice);
        Cambiato?.Invoke();
    }

    /// <summary>Anonimizza gli script non vuoti con il profilo in uso, controlla il risultato e salva la mappa aggiornata.</summary>
    public async Task AnonimizzaAsync()
    {
        var corrente = Corrente ?? throw new InvalidOperationException("Nessun profilo scelto.");
        var script = ScriptDaAnonimizzare;
        if (script.Count == 0) return;

        await _una.WaitAsync();
        try
        {
            Occupato = true;
            Cambiato?.Invoke();

            var sessione = await Task.Run(() =>
            {
                var risultato = AnonimizzatoreScript.Anonimizza(script, corrente.Profilo, corrente.Mappa);
                var testo = TestoPerIA.Componi(risultato, corrente.Mappa);
                var controllo = ControlloAntiFuga.Verifica(script, risultato, corrente.Mappa, corrente.Profilo);
                _archivio.Salva(corrente.Profilo, corrente.Mappa); // la mappa è cresciuta: i nomi nuovi devono restare
                return new SessioneAnonimizzazione(script, risultato, testo, controllo, corrente.Profilo.Id);
            });
            Anonimizzato = sessione;
        }
        finally
        {
            Occupato = false;
            _una.Release();
            Cambiato?.Invoke();
        }
    }

    // --- Ripristina ---

    public async Task RipristinaAsync()
    {
        var corrente = Corrente ?? throw new InvalidOperationException("Nessun profilo scelto.");
        if (string.IsNullOrWhiteSpace(RispostaIa)) return;
        var risposta = RispostaIa;

        await _una.WaitAsync();
        try
        {
            Occupato = true;
            Cambiato?.Invoke();
            var risultato = await Task.Run(() => new DeAnonimizzatore(corrente.Mappa).DeAnonimizza(risposta));
            Ripristinato = new SessioneRipristino(risposta, risultato, corrente.Profilo.Id);
        }
        finally
        {
            Occupato = false;
            _una.Release();
            Cambiato?.Invoke();
        }
    }

    // --- file ---

    private async Task SalvaFileAsync(ProfiloSalvato profilo)
    {
        await _una.WaitAsync();
        try
        {
            await Task.Run(() => _archivio.Salva(profilo.Profilo, profilo.Mappa));
        }
        finally
        {
            _una.Release();
        }
    }

    private Guid? LeggiProfiloCorrente()
    {
        try
        {
            if (!File.Exists(_fileImpostazioni)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(_fileImpostazioni));
            return doc.RootElement.TryGetProperty("profiloCorrente", out var v) && Guid.TryParse(v.GetString(), out var id) ? id : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // le impostazioni non sono essenziali: si riparte dal primo profilo
        }
    }

    private void ScriviProfiloCorrente(Guid? id)
    {
        try
        {
            Directory.CreateDirectory(opzioni.CartellaDati);
            File.WriteAllText(_fileImpostazioni, JsonSerializer.Serialize(new { profiloCorrente = id }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // non è grave: alla prossima apertura si parte dal primo profilo
        }
    }
}
