using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Che cosa sappiamo già di un nome: tabella, vista, procedura... Serve a classificare i riferimenti
/// (<c>FROM vOrdini</c> è una vista se da qualche parte c'è <c>CREATE VIEW vOrdini</c> o se la mappa lo sa già).
/// Si confronta il solo nome finale, senza schema, ignorando maiuscole e parentesi.
/// </summary>
public sealed class Conoscenza
{
    private readonly Dictionary<string, Ruolo> _ruoli = new(NomeSql.Confronto);

    private static readonly Ruolo[] RuoliDaMappa = [Ruolo.Tabella, Ruolo.Vista, Ruolo.Procedura, Ruolo.Funzione, Ruolo.Trigger];

    /// <summary>Parte da ciò che la mappa del cliente sa già dalle volte precedenti.</summary>
    public static Conoscenza Da(MappaCliente mappa)
    {
        var c = new Conoscenza();
        foreach (var voce in mappa.Voci.Where(v => RuoliDaMappa.Contains(v.Ruolo)))
            c._ruoli.TryAdd(voce.Originale, voce.Ruolo);
        return c;
    }

    /// <summary>Un <c>CREATE</c> trovato negli script ha la precedenza su ciò che dice la mappa.</summary>
    public void Dichiara(string nome, Ruolo ruolo) => _ruoli[NomeSql.Normalizza(nome)] = ruolo;

    public bool Contiene(string nome) => _ruoli.ContainsKey(NomeSql.Normalizza(nome));

    /// <summary>Ruolo per un nome usato dove ci si aspetta una tabella: vista se è nota come vista, altrimenti tabella.</summary>
    public Ruolo RuoloComeTabella(string nome) =>
        _ruoli.TryGetValue(NomeSql.Normalizza(nome), out var r) && r == Ruolo.Vista ? Ruolo.Vista : Ruolo.Tabella;
}
