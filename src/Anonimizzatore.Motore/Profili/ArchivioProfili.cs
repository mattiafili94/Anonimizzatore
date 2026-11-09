using System.Text.Json;
using System.Text.Json.Serialization;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Profili;

/// <summary>Un profilo con la sua mappa, come viene caricato dal disco.</summary>
public sealed record ProfiloSalvato(Profilo Profilo, MappaCliente Mappa);

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

    private sealed record FileProfilo(int Versione, Profilo Profilo, DatiMappa Mappa);

    public void Salva(Profilo profilo, MappaCliente mappa)
    {
        Directory.CreateDirectory(cartella);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new FileProfilo(VersioneFormato, profilo, mappa.Esporta()), OpzioniJson);
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
        return new ProfiloSalvato(file.Profilo, MappaCliente.Da(file.Mappa));
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

    public void Elimina(Guid id) => File.Delete(Percorso(id));

    private string Percorso(Guid id) => Path.Combine(cartella, id.ToString("D") + Estensione);
}
