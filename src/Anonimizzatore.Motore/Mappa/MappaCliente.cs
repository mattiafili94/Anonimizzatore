using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Commenti;

namespace Anonimizzatore.Motore.Mappa;

/// <summary>Una corrispondenza della mappa: nome originale (senza parentesi) ↔ nome anonimo.</summary>
public sealed record VoceMappa(Ruolo Ruolo, string Originale, string Anonimo, int Numero);

/// <summary>
/// Contenuto salvabile di una mappa (usato da <see cref="Profili.ArchivioProfili"/>). <c>Commenti</c> manca nei file
/// salvati prima della tappa 6: vale come "nessun commento".
/// </summary>
public sealed record DatiMappa(
    List<VoceMappa> Voci, Dictionary<string, int> Contatori, DatiCommenti? Commenti = null);

/// <summary>
/// Mappa di un cliente: nome originale ↔ nome anonimo, case-insensitive.
/// <list type="bullet">
/// <item>I nomi già assegnati restano <b>stabili</b>; solo <see cref="Rigenera"/> li azzera.</item>
/// <item>Un contatore per famiglia (<c>tbl</c>, <c>vw</c>, <c>col</c>…); colonne e variabili hanno un unico contatore
/// ciascuna, qualunque sia il tipo, così il numero da solo identifica il nome.</item>
/// <item>Password e SID non passano mai di qui: non hanno un <see cref="Ruolo"/>.</item>
/// </list>
/// </summary>
public sealed partial class MappaCliente
{
    private readonly Dictionary<string, VoceMappa> _perOriginale = new(NomeSql.Confronto); // "famiglia|nome"
    private readonly Dictionary<string, VoceMappa> _perAnonimo = new(NomeSql.Confronto);
    private readonly Dictionary<string, VoceMappa> _perNumero = new(NomeSql.Confronto);   // "famiglia|numero"
    private readonly Dictionary<string, int> _contatori = new(NomeSql.Confronto);
    private readonly HashSet<string> _nomiOriginali = new(NomeSql.Confronto);
    private readonly HashSet<string> _nomiRiservati = new(NomeSql.Confronto);

    /// <summary>
    /// I commenti del cliente: il testo originale di ogni commento sostituito da un segnaposto <c>/* c001 */</c>.
    /// Vive nella mappa (e quindi nel file cifrato del profilo) perché i commenti possono contenere dati riservati.
    /// </summary>
    public GestoreCommenti Commenti { get; private set; } = new();

    /// <summary>Tutte le corrispondenze, nell'ordine in cui sono state create.</summary>
    public IReadOnlyCollection<VoceMappa> Voci => _perAnonimo.Values;

    /// <summary>
    /// Nomi reali noti da altre fonti (per esempio lo schema importato): i nomi generati non coincideranno mai con
    /// questi. I nomi originali già in mappa sono esclusi automaticamente.
    /// </summary>
    public void AggiungiNomiRiservati(IEnumerable<string> nomi)
    {
        foreach (var nome in nomi) _nomiRiservati.Add(NomeSql.Normalizza(nome));
    }

    /// <summary>
    /// Restituisce il nome anonimo di <paramref name="nomeOriginale"/>: quello già assegnato se esiste, altrimenti ne
    /// crea uno nuovo.
    /// </summary>
    /// <param name="tipoSql">Tipo SQL (per colonne e variabili), es. <c>varchar(50)</c>. Conta solo la prima volta.</param>
    public string Ottieni(Ruolo ruolo, string nomeOriginale, string? tipoSql = null)
    {
        var nome = NomeSql.Normalizza(nomeOriginale);
        if (nome.Length == 0) throw new ArgumentException("Il nome è vuoto.", nameof(nomeOriginale));

        var famiglia = FamigliaRuolo.Di(ruolo);
        if (_perOriginale.TryGetValue($"{famiglia}|{nome}", out var esistente))
            return esistente.Anonimo;

        var codice = CodiceTipo.Da(tipoSql);
        var numero = _contatori.GetValueOrDefault(famiglia);
        string candidato;
        do
        {
            numero++;
            candidato = GeneratoreNomi.Formatta(ruolo, numero, codice, nome);
        } while (ECollisione(candidato) || candidato.Equals(nome, StringComparison.OrdinalIgnoreCase));

        _contatori[famiglia] = numero;
        Registra(new VoceMappa(ruolo, nome, candidato, numero));
        return candidato;
    }

    /// <summary>Vero se questo nome ha già un nome anonimo (assegnato in questa sessione o nelle precedenti).</summary>
    public bool Contiene(Ruolo ruolo, string nomeOriginale) =>
        _perOriginale.ContainsKey($"{FamigliaRuolo.Di(ruolo)}|{NomeSql.Normalizza(nomeOriginale)}");

    /// <summary>
    /// Cerca la voce di un nome anonimo (case-insensitive, con o senza parentesi).
    /// Per colonne e variabili basta il numero: <c>col_int_012</c> trova <c>col_vc50_012</c> anche se l'IA ha
    /// sbagliato il tipo.
    /// </summary>
    public VoceMappa? TrovaOriginale(string nomeAnonimo)
    {
        var nome = NomeSql.Normalizza(nomeAnonimo);
        if (_perAnonimo.TryGetValue(nome, out var voce)) return voce;

        var m = FormaColonnaOVariabile().Match(nome);
        if (!m.Success) return null;
        var famiglia = m.Groups["col"].Success ? "col" : "var";
        return _perNumero.GetValueOrDefault($"{famiglia}|{int.Parse(m.Groups["n"].Value)}");
    }

    /// <summary>"Rigenera nomi": svuota la mappa e riparte da 1. Da usare all'inizio di una nuova conversazione.</summary>
    public void Rigenera()
    {
        _perOriginale.Clear();
        _perAnonimo.Clear();
        _perNumero.Clear();
        _contatori.Clear();
        _nomiOriginali.Clear();
        Commenti.Rigenera();
    }

    public DatiMappa Esporta() => new([.. Voci], new Dictionary<string, int>(_contatori), Commenti.Esporta());

    public static MappaCliente Da(DatiMappa dati)
    {
        var mappa = new MappaCliente();
        if (dati.Commenti is not null) mappa.Commenti = GestoreCommenti.Da(dati.Commenti);
        foreach (var voce in dati.Voci) mappa.Registra(voce);
        foreach (var (famiglia, n) in dati.Contatori) mappa._contatori[famiglia] = n;
        return mappa;
    }

    private void Registra(VoceMappa voce)
    {
        var famiglia = FamigliaRuolo.Di(voce.Ruolo);
        _perOriginale[$"{famiglia}|{voce.Originale}"] = voce;
        _perAnonimo[voce.Anonimo] = voce;
        _perNumero[$"{famiglia}|{voce.Numero}"] = voce;
        _nomiOriginali.Add(voce.Originale);
    }

    private bool ECollisione(string candidato) =>
        _perAnonimo.ContainsKey(candidato) || _nomiOriginali.Contains(candidato) || _nomiRiservati.Contains(candidato);

    [GeneratedRegex(@"^(?:(?<col>col_)|@[vp]_)\w*?_(?<n>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FormaColonnaOVariabile();
}
