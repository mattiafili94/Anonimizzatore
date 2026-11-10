namespace Anonimizzatore.Interfaccia.Componenti;

public enum TipoSegnalazione
{
    Info,
    Successo,
    Attenzione,
    Errore,
}

/// <summary>Una voce dell'elenco degli avvisi: il testo e, se c'è, dove (script e riga).</summary>
public sealed record VoceAvviso(TipoSegnalazione Tipo, string Testo, string? Posizione = null);
