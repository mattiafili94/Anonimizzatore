using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Lettura;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Commenti;

/// <summary>Un commento conservato: numero del segnaposto (1 = <c>/* c001 */</c>) e testo originale completo (con <c>--</c> o <c>/* */</c>).</summary>
public sealed record CommentoConservato(int Numero, string Testo);

/// <summary>Dati di <see cref="GestoreCommenti"/> in forma serializzabile (JSON), da salvare nella mappa del cliente.</summary>
public sealed record DatiCommenti(List<CommentoConservato> Commenti);

/// <summary>Esito di <see cref="GestoreCommenti.Ripristina"/>.</summary>
/// <param name="Testo">Il testo con i commenti originali rimessi al loro posto.</param>
/// <param name="Persi">Numeri dei segnaposto noti che nel testo non ci sono più (l'IA li ha tolti): da segnalare nel report.</param>
/// <param name="Sconosciuti">Segnaposto presenti nel testo ma mai assegnati (numero non in mappa): lasciati come sono.</param>
/// <param name="Spostati">Segnaposto di commenti <c>--</c> trovati a metà riga (l'IA ha aggiunto codice dopo): per non
/// "mangiare" quel codice, dopo il commento è stato inserito un a capo.</param>
public sealed record RisultatoRipristino(
    string Testo,
    IReadOnlyList<int> Persi,
    IReadOnlyList<int> Sconosciuti,
    IReadOnlyList<int> Spostati)
{
    /// <summary>I segnaposto persi nel formato che compare nel testo (<c>/* c001 */</c>), per i messaggi del report.</summary>
    public IReadOnlyList<string> PersiComeSegnaposto => [.. Persi.Select(GestoreCommenti.Segnaposto)];
}

/// <summary>
/// Livello 5: i commenti (<c>-- ...</c> e <c>/* ... */</c>) diventano segnaposto <c>/* c001 */</c>, <c>/* c002 */</c>…
/// e il testo originale resta qui, per rimetterlo al suo posto nella risposta dell'IA.
/// <para>
/// Un'istanza appartiene a un cliente: la numerazione prosegue tra uno script e l'altro, così i segnaposto non si
/// confondono e si possono riapplicare anche più tardi (<see cref="Esporta"/> / <see cref="Da"/> / <see cref="ToJson"/>).
/// Lavora su un testo qualsiasi: anche una stringa già estratta da un SQL dinamico.
/// </para>
/// </summary>
public sealed partial class GestoreCommenti
{
    // Tollerante verso ciò che l'IA può combinare: maiuscole, spazi diversi o assenti, più cifre.
    [GeneratedRegex(@"/\*\s*[cC](\d+)\s*\*/")]
    private static partial Regex RegexSegnaposto();

    private readonly SortedDictionary<int, string> _commenti = [];
    private readonly Dictionary<string, int> _numeroPerTesto = new(StringComparer.Ordinal);
    private int _ultimoNumero;

    /// <summary>I commenti conservati, in ordine di numero.</summary>
    public IReadOnlyList<CommentoConservato> Commenti =>
        [.. _commenti.Select(c => new CommentoConservato(c.Key, c.Value))];

    /// <summary>Il segnaposto per un numero: <c>/* c001 */</c> (almeno tre cifre).</summary>
    public static string Segnaposto(int numero) => $"/* c{numero:D3} */";

    /// <summary>Anonimizza un testo T-SQL (o un frammento, per esempio il contenuto di un SQL dinamico).</summary>
    public string Anonimizza(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);
        return Anonimizza(testo, Tokenizza(testo));
    }

    // Solo il lessico, senza albero: basta per i commenti e funziona anche su frammenti non validi
    // (per esempio un commento /* mai chiuso, per cui l'intero parsing non produce token).
    private static IList<TSqlParserToken> Tokenizza(string testo)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var lettore = new StringReader(testo);
        return parser.GetTokenStream(lettore, out _);
    }

    /// <summary>Anonimizza uno script già letto, usando i suoi token: tutto il resto resta identico, carattere per carattere.</summary>
    public string Anonimizza(ScriptLetto script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return Anonimizza(script.TestoOriginale, script.Token ?? Tokenizza(script.TestoOriginale));
    }

    private string Anonimizza(string testo, IList<TSqlParserToken> tokens)
    {
        var risultato = new StringBuilder(testo.Length);
        var coperti = 0;
        foreach (var token in tokens)
        {
            var testoToken = token.Text ?? "";
            coperti += testoToken.Length;
            if (token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                // Il token di un "--" non comprende l'a capo finale: resta al suo posto dopo il segnaposto.
                Conserva(risultato, testoToken);
            }
            else
            {
                risultato.Append(testoToken);
            }
        }

        // Un commento /* mai chiuso (arriva fino alla fine del testo) non produce nessun token: il lessico di
        // ScriptDom lo scarta. Lo si recupera dal testo rimasto fuori, così non si perde né resta in chiaro.
        if (coperti < testo.Length && testo.AsSpan(0, coperti).SequenceEqual(string.Concat(tokens.Select(t => t.Text ?? ""))))
        {
            var coda = testo[coperti..];
            if (coda.StartsWith("/*", StringComparison.Ordinal))
                Conserva(risultato, coda);
            else
                risultato.Append(coda);
        }
        return risultato.ToString();
    }

    private void Conserva(StringBuilder risultato, string commento)
    {
        // Lo stesso testo riceve sempre lo stesso segnaposto: rianonimizzare lo stesso script (o ripetere "-- TODO")
        // non cambia i numeri a metà di una conversazione con l'IA e non fa crescere la mappa.
        if (!_numeroPerTesto.TryGetValue(commento, out var numero))
        {
            numero = ++_ultimoNumero;
            _commenti[numero] = commento;
            _numeroPerTesto[commento] = numero;
        }
        risultato.Append(Segnaposto(numero));
    }

    /// <summary>
    /// Rimette al loro posto i commenti originali. Funziona anche dopo che l'IA ha rimaneggiato i segnaposto
    /// (<c>/*c001*/</c>, <c>/* C001 */</c>, <c>/* c0001 */</c>). Si può chiamare su qualsiasi testo, anche in momenti diversi.
    /// </summary>
    public RisultatoRipristino Ripristina(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        var trovati = new HashSet<int>();
        var sconosciuti = new SortedSet<int>();
        var spostati = new SortedSet<int>();

        var nuovo = RegexSegnaposto().Replace(testo, m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var numero) || !_commenti.TryGetValue(numero, out var originale))
            {
                if (int.TryParse(m.Groups[1].Value, out var n))
                    sconosciuti.Add(n);
                return m.Value;
            }

            trovati.Add(numero);

            // Un commento "--" arriva fino a fine riga: se dopo il segnaposto c'è altro sulla stessa riga
            // (l'IA ha aggiunto codice), lo si manderebbe in commento. Si va a capo dopo il commento.
            if (originale.StartsWith("--", StringComparison.Ordinal) && !FineRiga(testo, m.Index + m.Length))
            {
                spostati.Add(numero);
                return originale + AcapoDi(testo);
            }
            return originale;
        });

        var persi = _commenti.Keys.Where(k => !trovati.Contains(k)).ToList();
        return new RisultatoRipristino(nuovo, persi, [.. sconosciuti], [.. spostati]);
    }

    /// <summary>Vero se dopo la posizione indicata la riga finisce (solo spazi fino all'a capo o alla fine del testo).</summary>
    private static bool FineRiga(string testo, int posizione)
    {
        for (var i = posizione; i < testo.Length; i++)
        {
            if (testo[i] is '\r' or '\n') return true;
            if (!char.IsWhiteSpace(testo[i])) return false;
        }
        return true;
    }

    private static string AcapoDi(string testo) => testo.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Esporta lo stato (per la mappa del cliente).</summary>
    public DatiCommenti Esporta() => new([.. Commenti]);

    /// <summary>Ricostruisce un gestore da dati salvati: la numerazione riparte dopo l'ultimo commento.</summary>
    public static GestoreCommenti Da(DatiCommenti dati)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var gestore = new GestoreCommenti();
        foreach (var c in dati.Commenti)
        {
            gestore._commenti[c.Numero] = c.Testo;
            gestore._numeroPerTesto.TryAdd(c.Testo, c.Numero);
            gestore._ultimoNumero = Math.Max(gestore._ultimoNumero, c.Numero);
        }
        return gestore;
    }

    /// <summary>Stato in JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(Esporta());

    /// <summary>Ricostruisce un gestore dal JSON prodotto da <see cref="ToJson"/>.</summary>
    public static GestoreCommenti DaJson(string json) =>
        Da(JsonSerializer.Deserialize<DatiCommenti>(json) ?? throw new JsonException("JSON dei commenti vuoto."));

    /// <summary>Dimentica tutti i commenti e riparte da <c>c001</c> ("rigenera nomi").</summary>
    public void Rigenera()
    {
        _commenti.Clear();
        _numeroPerTesto.Clear();
        _ultimoNumero = 0;
    }
}
