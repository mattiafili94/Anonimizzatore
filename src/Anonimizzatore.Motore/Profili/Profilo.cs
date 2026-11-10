using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Profili;

/// <summary>Per ogni tipo di oggetto: anonimizzarlo o lasciarlo in chiaro.</summary>
public enum Trattamento
{
    Anonimizza,
    InChiaro,
}

/// <summary>Impostazioni di un cliente: matrice per tipo, whitelist e blacklist.</summary>
public sealed class Profilo
{
    /// <summary>Identificativo interno: dà il nome al file, così il nome del cliente non compare nel nome del file.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Nome { get; set; }

    /// <summary>Matrice per tipo di oggetto. I ruoli mancanti usano <see cref="MatriceDefault"/>.</summary>
    public Dictionary<Ruolo, Trattamento> Matrice { get; init; } = new(MatriceDefault);

    /// <summary>Nomi interi o parole che restano in chiaro (in aggiunta alla lista base).</summary>
    public List<string> Whitelist { get; init; } = [];

    /// <summary>Parole sempre anonimizzate, anche dentro colonne, variabili e stringhe. Vince sulla whitelist.</summary>
    public List<string> Blacklist { get; init; } = [];

    /// <summary>
    /// Nelle stringhe (messaggi, testi, nomi di backup, URL) si sostituiscono i nomi già anonimizzati e le parole della blacklist
    /// (<c>txt_001</c>); il resto del testo resta com'è. Acceso di default.
    /// </summary>
    public bool NomiNelleStringhe { get; set; } = true;

    public Trattamento TrattamentoDi(Ruolo ruolo) =>
        Matrice.TryGetValue(ruolo, out var t) ? t : MatriceDefault[ruolo];

    /// <summary>
    /// Default confermati: oggetti, sicurezza e infrastruttura anonimizzati; colonne, variabili, parametri,
    /// temporanee, indici e constraint in chiaro (salvo blacklist o parti che sono oggetti anonimizzati).
    /// </summary>
    public static IReadOnlyDictionary<Ruolo, Trattamento> MatriceDefault { get; } =
        Enum.GetValues<Ruolo>().ToDictionary(r => r, r => r switch
        {
            Ruolo.Colonna or Ruolo.Variabile or Ruolo.Parametro or Ruolo.Temporanea or Ruolo.Alias
                or Ruolo.Indice or Ruolo.PrimaryKey or Ruolo.ForeignKey
                or Ruolo.Default or Ruolo.Check or Ruolo.Unique => Trattamento.InChiaro,
            _ => Trattamento.Anonimizza,
        });
}
