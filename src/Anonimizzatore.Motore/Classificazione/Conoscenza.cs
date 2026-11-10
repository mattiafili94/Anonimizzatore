using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Che cosa sappiamo già dei nomi: tabella, vista, procedura... (serve a classificare i riferimenti: <c>FROM vOrdini</c>
/// è una vista se da qualche parte c'è <c>CREATE VIEW vOrdini</c> o se la mappa lo sa già), i tipi di colonne e
/// variabili e che tipo di constraint è un nome. Gli oggetti si confrontano per il solo nome finale, senza schema,
/// ignorando maiuscole e parentesi.
/// </summary>
public sealed class Conoscenza
{
    private readonly Dictionary<string, Ruolo> _ruoli = new(NomeSql.Confronto);
    private readonly Dictionary<string, Ruolo> _vincoli = new(NomeSql.Confronto);
    private readonly Dictionary<string, string> _tipiDichiarati = new(NomeSql.Confronto);
    private readonly Dictionary<string, string> _tipiDedotti = new(NomeSql.Confronto);

    private static readonly Ruolo[] RuoliDaMappa = [Ruolo.Tabella, Ruolo.Vista, Ruolo.Procedura, Ruolo.Funzione, Ruolo.Trigger];

    /// <summary>Parte da ciò che la mappa del cliente sa già dalle volte precedenti.</summary>
    public static Conoscenza Da(MappaCliente mappa)
    {
        var c = new Conoscenza();
        foreach (var voce in mappa.Voci.Where(v => RuoliDaMappa.Contains(v.Ruolo)))
            c._ruoli.TryAdd(voce.Originale, voce.Ruolo);
        foreach (var voce in mappa.Voci.Where(v => v.Ruolo is Ruolo.RuoloDb or Ruolo.Utente or Ruolo.Login))
            c._principali.TryAdd(NomeSql.Normalizza(voce.Originale), voce.Ruolo);
        return c;
    }

    // --- oggetti ---

    /// <summary>Un <c>CREATE</c> trovato negli script ha la precedenza su ciò che dice la mappa.</summary>
    public void Dichiara(string nome, Ruolo ruolo) => _ruoli[NomeSql.Normalizza(nome)] = ruolo;

    public bool Contiene(string nome) => _ruoli.ContainsKey(NomeSql.Normalizza(nome));

    /// <summary>Ruolo per un nome usato dove ci si aspetta una tabella: vista se è nota come vista, altrimenti tabella.</summary>
    public Ruolo RuoloComeTabella(string nome) =>
        _ruoli.TryGetValue(NomeSql.Normalizza(nome), out var r) && r == Ruolo.Vista ? Ruolo.Vista : Ruolo.Tabella;

    /// <summary>Ruolo noto di un oggetto (tabella, vista, procedura, funzione, trigger), se è già stato visto.</summary>
    public Ruolo? RuoloNoto(string nome) =>
        _ruoli.TryGetValue(NomeSql.Normalizza(nome), out var r) ? r : null;

    // --- login, utenti e ruoli ---

    private readonly Dictionary<string, Ruolo> _principali = new(NomeSql.Confronto);

    /// <summary>
    /// Registra un login, un utente o un ruolo dichiarato (<c>CREATE ROLE</c>...). Serve per i nomi che compaiono dove
    /// può esserci sia un utente sia un ruolo (<c>GRANT ... TO nome</c>): se il nome è un ruolo noto prende il
    /// prefisso <c>rol_</c>, altrimenti <c>usr_</c>.
    /// </summary>
    public void DichiaraPrincipale(string nome, Ruolo ruolo) => _principali[NomeSql.Normalizza(nome)] = ruolo;

    /// <summary>Ruolo di un nome che può essere utente o ruolo: <see cref="Ruolo.RuoloDb"/> se è un ruolo noto, altrimenti <see cref="Ruolo.Utente"/>.</summary>
    public Ruolo RuoloPrincipale(string nome) =>
        _principali.TryGetValue(NomeSql.Normalizza(nome), out var r) && r == Ruolo.RuoloDb ? Ruolo.RuoloDb : Ruolo.Utente;

    // --- constraint e indici ---

    public void DichiaraVincolo(string nome, Ruolo ruolo) => _vincoli.TryAdd(NomeSql.Normalizza(nome), ruolo);

    /// <summary>Tipo di constraint/indice di un nome già visto in un <c>CREATE</c> o <c>ADD CONSTRAINT</c>, se noto.</summary>
    public Ruolo? RuoloVincolo(string nome) =>
        _vincoli.TryGetValue(NomeSql.Normalizza(nome), out var r) ? r : null;

    // --- tipi di colonne, variabili e parametri ---

    /// <summary>
    /// Registra il tipo di una colonna o variabile. I tipi <i>dichiarati</i> (CREATE TABLE, DECLARE, parametri) hanno
    /// la precedenza su quelli <i>dedotti</i> da un CAST/CONVERT. Vale il primo di ciascun tipo.
    /// </summary>
    public void DichiaraTipo(Ruolo ruolo, string nome, string? tipoSql, bool dedotto = false)
    {
        if (string.IsNullOrWhiteSpace(tipoSql)) return;
        (dedotto ? _tipiDedotti : _tipiDichiarati).TryAdd(ChiaveTipo(ruolo, nome), tipoSql);
    }

    public string? TipoDi(Ruolo ruolo, string nome)
    {
        var chiave = ChiaveTipo(ruolo, nome);
        return _tipiDichiarati.TryGetValue(chiave, out var t) || _tipiDedotti.TryGetValue(chiave, out t) ? t : null;
    }

    private static string ChiaveTipo(Ruolo ruolo, string nome) => FamigliaRuolo.Di(ruolo) + "|" + NomeSql.Normalizza(nome);
}

/// <summary>Scrive un tipo di ScriptDom come testo (<c>varchar(50)</c>, <c>decimal(18,2)</c>) per <see cref="CodiceTipo"/>.</summary>
public static class TipiSql
{
    public static string? Formatta(DataTypeReference? tipo)
    {
        if (tipo is XmlDataTypeReference) return "xml";
        if (tipo?.Name?.BaseIdentifier is not { } nome) return null;

        return tipo is ParameterizedDataTypeReference { Parameters.Count: > 0 } p
            ? $"{nome.Value}({string.Join(",", p.Parameters.Select(x => x.Value))})"
            : nome.Value;
    }
}
