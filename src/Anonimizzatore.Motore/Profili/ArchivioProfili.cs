using System.Text.Json;
using System.Text.Json.Serialization;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.SchemaDb;

namespace Anonimizzatore.Motore.Profili;

/// <summary>Un profilo con la sua mappa, come viene caricato dal disco.</summary>
/// <param name="Schema">Lo schema del database importato (facoltativo): si salva insieme al profilo, cifrato.</param>
public sealed record ProfiloSalvato(Profilo Profilo, MappaCliente Mappa)
{
    public SchemaDatabase? Schema { get; set; }
}

/// <summary>
/// Salva e carica i profili: un file cifrato <c>{Id}.anonprofile</c> per cliente, con profilo e mappa insieme.
/// La cartella la decide chi lo usa (nell'app: <c>%APPDATA%\Anonimizzatore</c>).
/// </summary>
public sealed class ArchivioProfili(string cartella, IProtezioneDati protezione)
{
    public const string Estensione = ".anonprofile";
    private const int VersioneFormato = 1;

    private static readonly JsonSerializerOptions OpzioniJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record FileProfilo(int Versione, Profilo Profilo, DatiMappa Mappa, SchemaDatabase? Schema = null);

    public void Salva(ProfiloSalvato profilo) => Salva(profilo.Profilo, profilo.Mappa, profilo.Schema);

    public void Salva(Profilo profilo, MappaCliente mappa, SchemaDatabase? schema = null)
    {
        Directory.CreateDirectory(cartella);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new FileProfilo(VersioneFormato, profilo, mappa.Esporta(), schema), OpzioniJson);
        var cifrato = protezione.Proteggi(json);

        // Scrittura sicura: prima un file temporaneo, poi lo scambio. Se qualcosa va storto il vecchio file resta integro.
        var percorso = Percorso(profilo.Id);
        var temporaneo = percorso + ".tmp";
        File.WriteAllBytes(temporaneo, cifrato);
        File.Move(temporaneo, percorso, overwrite: true);
    }

    public ProfiloSalvato Carica(Guid id)
    {
        var json = protezione.Sblocca(File.ReadAllBytes(Percorso(id)));
        var file = JsonSerializer.Deserialize<FileProfilo>(json, OpzioniJson)
                   ?? throw new InvalidDataException("File del profilo vuoto o non valido.");
        if (file.Versione != VersioneFormato)
            throw new InvalidDataException($"Versione del file non supportata: {file.Versione}.");
        return new ProfiloSalvato(file.Profilo, MappaCliente.Da(file.Mappa)) { Schema = file.Schema };
    }

    /// <summary>Tutti i profili presenti nella cartella.</summary>
    public IReadOnlyList<ProfiloSalvato> CaricaTutti()
    {
        if (!Directory.Exists(cartella)) return [];
        return Directory.EnumerateFiles(cartella, "*" + Estensione)
            .Select(f => Guid.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .Select(Carica)
            .OrderBy(p => p.Profilo.Nome, NomeSql.Confronto)
            .ToList();
    }

    /// <summary>
    /// Come <see cref="CaricaTutti"/>, ma un file che non si legge (cifrato da un altro utente o su un altro PC, danneggiato)
    /// non ferma gli altri: finisce tra gli errori, con il nome del file e il motivo.
    /// </summary>
    public (IReadOnlyList<ProfiloSalvato> Profili, IReadOnlyList<string> Errori) CaricaTuttiTollerante()
    {
        var profili = new List<ProfiloSalvato>();
        var errori = new List<string>();
        if (Directory.Exists(cartella))
            foreach (var file in Directory.EnumerateFiles(cartella, "*" + Estensione))
            {
                try
                {
                    if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id)) continue;
                    profili.Add(Carica(id));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException
                                              or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    errori.Add($"{Path.GetFileName(file)}: {e.Message}");
                }
            }
        return ([.. profili.OrderBy(p => p.Profilo.Nome, NomeSql.Confronto)], errori);
    }

    public void Elimina(Guid id) => File.Delete(Percorso(id));

    private string Percorso(Guid id) => Path.Combine(cartella, id.ToString("D") + Estensione);
}
