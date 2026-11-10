namespace Anonimizzatore.Motore.Infrastruttura;

/// <summary>Che tipo di dato di infrastruttura è stato trovato.</summary>
public enum TipoInfrastruttura
{
    PercorsoUnc,
    PercorsoLocale,
    Ip,
    Host,
    Email,
    Url,
    StringaConnessione,
}

/// <summary>Una sostituzione fatta: <see cref="Posizione"/> è l'indice nel testo ORIGINALE.</summary>
public sealed record Sostituzione(TipoInfrastruttura Tipo, string Originale, string Anonimo, int Posizione);

/// <summary>Esito di <see cref="AnalizzatoreInfrastruttura.Anonimizza"/>.</summary>
public sealed record RisultatoInfrastruttura(
    string Testo,
    IReadOnlyList<Sostituzione> Sostituzioni,
    IReadOnlyList<string> Avvisi);

/// <summary>Esito di <see cref="AnalizzatoreInfrastruttura.Ripristina"/>.</summary>
/// <param name="NonTrovati">Nomi con la forma giusta (es. <c>dir_099</c>) ma assenti dalla mappa: lasciati come sono.</param>
public sealed record RisultatoRipristino(
    string Testo,
    IReadOnlyList<Sostituzione> Sostituzioni,
    IReadOnlyList<string> NonTrovati);

/// <summary>Opzioni dell'analizzatore.</summary>
/// <param name="TestoLibero">
/// <c>false</c> (default): il testo è il contenuto di un letterale stringa, quindi un percorso può contenere spazi
/// (<c>D:\Dati Clienti\Vendite 2026.bak</c>). <c>true</c>: il testo è prosa, quindi uno spazio chiude il percorso
/// (tranne nei nomi di sistema come <c>Program Files</c>).
/// </param>
public sealed record OpzioniInfrastruttura(bool TestoLibero = false);
