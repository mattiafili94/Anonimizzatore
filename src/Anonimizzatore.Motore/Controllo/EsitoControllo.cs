namespace Anonimizzatore.Motore.Controllo;

/// <summary>Che cosa è rimasto dove non doveva.</summary>
public enum TipoFuga
{
    /// <summary>Il nome vero di un oggetto anonimizzato compare ancora (in una stringa, in una riga SQLCMD o come identificatore).</summary>
    NomeOriginale,

    /// <summary>Una parola della blacklist del profilo.</summary>
    ParolaBlacklist,

    /// <summary>Una password (o un suo hash) che non è stata cancellata.</summary>
    Segreto,

    /// <summary>Email, codice fiscale, IBAN, carta di credito, IP o URL in una stringa: i dati (livello 4) non sono nella v1.</summary>
    DatoPersonale,

    /// <summary>Un commento che non è un segnaposto <c>/* c001 */</c>: il suo testo non è stato sostituito.</summary>
    CommentoNonSostituito,

    /// <summary>Lo script originale si leggeva e quello anonimizzato no.</summary>
    ScriptNonValido,
}

public enum GravitaFuga
{
    /// <summary>Da guardare, ma può essere voluto (una colonna lasciata in chiaro) o fuori dalla v1 (dati).</summary>
    Avviso,

    /// <summary>Quasi certamente un dato che non deve uscire: la copia resta bloccata finché l'utente non conferma.</summary>
    Bloccante,
}

/// <param name="Valore">Il nome o la parola trovata. Per un segreto è solo la parola chiave (<c>PASSWORD</c>): il valore non si ripete mai.</param>
/// <param name="Script">Posizione (da 0) dello script in cui è stato trovato, oppure -1 per lo schema allegato.</param>
/// <param name="Riga">Riga (da 1) nel testo anonimizzato di quello script.</param>
public sealed record Fuga(TipoFuga Tipo, GravitaFuga Gravita, string Valore, int Script, int Riga, string Messaggio);

/// <summary>Esito di <see cref="ControlloAntiFuga.Verifica"/>.</summary>
public sealed record EsitoControllo(IReadOnlyList<Fuga> Fughe)
{
    /// <summary>Vero se non è stato trovato niente.</summary>
    public bool Pulito => Fughe.Count == 0;

    /// <summary>Vero se la copia va bloccata finché l'utente non conferma (c'è almeno una fuga bloccante).</summary>
    public bool RichiedeConferma => Fughe.Any(f => f.Gravita == GravitaFuga.Bloccante);
}
