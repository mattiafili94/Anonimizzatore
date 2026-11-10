using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Anonimizzazione;

/// <summary>Esito per un singolo script.</summary>
/// <param name="Testo">Lo script anonimizzato; <c>null</c> se non è stato possibile (es. errori di sintassi).</param>
public sealed record ScriptAnonimizzato(string? Testo, IReadOnlyList<Avviso> Avvisi)
{
    public bool Anonimizzato => Testo is not null;
}

public sealed record RisultatoAnonimizzazione(IReadOnlyList<ScriptAnonimizzato> Script);

/// <summary>
/// Punto d'ingresso del motore. Tutti gli script di una chiamata condividono la stessa mappa. Passi:
/// <list type="number">
/// <item>lettura (ScriptDom) e primo giro su tutti gli script (CREATE, tipi, constraint, alias);</item>
/// <item>classificazione dei nomi di ogni script;</item>
/// <item>decisione per ogni nome: sistema → in chiaro; blacklist → anonimizzato; altrimenti la matrice del profilo;</item>
/// <item>fase A: nomi di oggetti, colonne, variabili e alias; fase B: nomi composti di indici e constraint
/// (<c>FK_Righe_Ordini</c> → <c>FK_tbl_001_tbl_002</c>), quando tutti gli oggetti hanno già il loro nome anonimo;</item>
/// <item>sostituzione sui token del testo originale.</item>
/// </list>
/// </summary>
/// <remarks>
/// Nome diverso da "Anonimizzatore" apposta: sarebbe uguale al primo pezzo del namespace e creerebbe ambiguità.
/// </remarks>
public static class AnonimizzatoreScript
{
    // Ruoli i cui nomi sono "composti" (IX_Ordini_Data): se non vanno anonimizzati per intero si sostituiscono le parti.
    private static readonly HashSet<Ruolo> RuoliComposti =
        [Ruolo.Indice, Ruolo.PrimaryKey, Ruolo.ForeignKey, Ruolo.Default, Ruolo.Check, Ruolo.Unique];

    // Ruoli di cui le parti dei nomi composti vengono sostituite.
    private static readonly HashSet<Ruolo> RuoliDelleParti =
    [
        Ruolo.Tabella, Ruolo.Vista, Ruolo.Procedura, Ruolo.Funzione, Ruolo.Trigger,
        Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer, Ruolo.Server,
    ];

    private sealed record Elaborato(
        ScriptLetto Letto, PreAnalisi? Pre, IReadOnlyList<NomeTrovato> Nomi, IReadOnlyList<Avviso> Avvisi)
    {
        public Dictionary<int, string> Sostituzioni { get; } = [];
    }

    public static RisultatoAnonimizzazione Anonimizza(IReadOnlyList<string> script, Profilo profilo, MappaCliente mappa)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(profilo);
        ArgumentNullException.ThrowIfNull(mappa);

        var letti = script.Select(LettoreScript.Leggi).ToList();

        // I nomi veri presenti negli script non devono mai coincidere con un nome generato.
        foreach (var letto in letti)
            mappa.AggiungiNomiRiservati(letto.Token
                .Where(t => t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                .Select(t => t.Text));

        // Giro 1 su tutti gli script: i CREATE e i tipi di uno servono a classificare i riferimenti degli altri.
        var conoscenza = Conoscenza.Da(mappa);
        var pre = new Dictionary<ScriptLetto, PreAnalisi>();
        foreach (var letto in letti.Where(l => l.Valido))
        {
            var p = new PreAnalisi(conoscenza);
            letto.Albero.Accept(p);
            pre[letto] = p;
        }

        // Giro 2: classificazione.
        var elaborati = letti.Select(l =>
        {
            if (!pre.TryGetValue(l, out var p)) return new Elaborato(l, null, [], []);
            var (nomi, avvisi) = Classificatore.Classifica(l.Albero, conoscenza, p);
            return new Elaborato(l, p, nomi, avvisi);
        }).ToList();

        var liste = new Liste(profilo, mappa);

        // Fase A: oggetti, colonne, variabili e alias, nell'ordine in cui compaiono.
        foreach (var e in elaborati)
            foreach (var nome in e.Nomi.Where(n => !RuoliComposti.Contains(n.Ruolo) && liste.DaAnonimizzare(n)))
                e.Sostituzioni[nome.IndiceToken] =
                    Riquota(mappa.Ottieni(nome.Ruolo, nome.Nome, TipoDi(nome, conoscenza)), nome.Apici);

        // Fase B: indici e constraint.
        var parti = PartiSostituibili(mappa);
        foreach (var e in elaborati)
            foreach (var nome in e.Nomi.Where(n => RuoliComposti.Contains(n.Ruolo) && !n.DiSistema))
            {
                // Per intero se la blacklist o la matrice lo chiedono, o se le parti non si possono sostituire in modo
                // pulito; altrimenti solo le parti che sono oggetti anonimizzati.
                var nuovo = liste.DaAnonimizzare(nome) ? null : SostituisciParti(nome.Nome, parti);
                nuovo ??= mappa.Ottieni(nome.Ruolo, nome.Nome);
                if (nuovo != nome.Nome) e.Sostituzioni[nome.IndiceToken] = Riquota(nuovo, nome.Apici);
            }

        return new RisultatoAnonimizzazione([.. elaborati.Select(Ricomponi)]);
    }

    private static ScriptAnonimizzato Ricomponi(Elaborato e)
    {
        if (e.Pre is null)
        {
            // La modalità di riserva (solo token) arriva nella tappa 10: per ora meglio non restituire nulla
            // che potrebbe contenere nomi in chiaro.
            var primo = e.Letto.Errori[0];
            return new ScriptAnonimizzato(null,
                [new Avviso($"Script con errori di sintassi, non anonimizzato: {primo.Message}", primo.Line)]);
        }

        var testi = e.Letto.Token.Select(t => t.Text).ToArray();
        foreach (var (indice, testo) in e.Sostituzioni) testi[indice] = testo;
        return new ScriptAnonimizzato(string.Concat(testi), e.Avvisi);
    }

    /// <summary>
    /// La decisione per ogni nome, in quest'ordine: oggetto di sistema → in chiaro; blacklist → anonimizzato (vince
    /// su tutto); tipo di oggetto in chiaro nella matrice → in chiaro; whitelist → in chiaro, salvo che il nome abbia
    /// già un nome anonimo (i nomi assegnati restano stabili: si cambiano solo con "rigenera nomi"); altrimenti
    /// anonimizzato.
    /// </summary>
    private sealed class Liste(Profilo profilo, MappaCliente mappa)
    {
        private readonly Blacklist _blacklist = new(profilo.Blacklist);
        private readonly Whitelist _whitelist = new(profilo.Whitelist);

        public bool DaAnonimizzare(NomeTrovato nome)
        {
            if (nome.DiSistema) return false;
            if (_blacklist.Contiene(nome.Nome)) return true;
            if (profilo.TrattamentoDi(nome.Ruolo) != Trattamento.Anonimizza) return false;
            return !_whitelist.Contiene(nome.Nome) || mappa.Contiene(nome.Ruolo, nome.Nome);
        }
    }

    /// <summary>Tipo SQL noto per colonne, variabili e parametri (serve al nome: <c>col_vc50_012</c>).</summary>
    private static string? TipoDi(NomeTrovato nome, Conoscenza conoscenza) =>
        nome.Ruolo is Ruolo.Colonna or Ruolo.Variabile or Ruolo.Parametro
            ? conoscenza.TipoDi(nome.Ruolo, nome.Nome)
            : null;

    // --- nomi composti ---

    /// <summary>Nomi originali di oggetti già anonimizzati con il loro nome anonimo, dal più lungo al più corto.</summary>
    private static IReadOnlyList<(string Originale, string Anonimo)> PartiSostituibili(MappaCliente mappa) =>
        [.. mappa.Voci
            .Where(v => RuoliDelleParti.Contains(v.Ruolo))
            .Select(v => (v.Originale, v.Anonimo))
            .OrderByDescending(v => v.Originale.Length)];

    /// <summary>
    /// Sostituisce nel nome le parti che sono oggetti anonimizzati. Una parte "pulita" è delimitata da underscore o
    /// punteggiatura: <c>IX_Ordini_Data</c> → <c>IX_tbl_001_Data</c>. Restituisce <c>null</c> se una parte è attaccata
    /// ad altro (<c>IXOrdini</c>, <c>IX_OrdiniExtra</c>, <c>IX_Ordini2</c>): sostituendola il nome anonimo si
    /// confonderebbe con ciò che lo circonda e il ripristino non darebbe più il nome esatto, quindi in quel caso va
    /// anonimizzato il nome intero. <c>IX_Ordinine</c> invece non contiene la parte <c>Ordini</c> e resta com'è.
    /// </summary>
    internal static string? SostituisciParti(string nome, IReadOnlyList<(string Originale, string Anonimo)> parti)
    {
        if (parti.Count == 0) return nome;

        var risultato = new System.Text.StringBuilder(nome.Length + 16);
        var i = 0;
        while (i < nome.Length)
        {
            (string Originale, string Anonimo)? trovato = null;
            if (InizioParola(nome, i))
                foreach (var parte in parti)
                    if (i + parte.Originale.Length <= nome.Length
                        && string.Compare(nome, i, parte.Originale, 0, parte.Originale.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && FineParola(nome, i + parte.Originale.Length))
                    {
                        trovato = parte;
                        break;
                    }

            if (trovato is { } t)
            {
                var fine = i + t.Originale.Length;
                var pulita = (i == 0 || !char.IsLetterOrDigit(nome[i - 1]))
                             && (fine >= nome.Length || !char.IsLetterOrDigit(nome[fine]));
                if (!pulita) return null;

                risultato.Append(t.Anonimo);
                i = fine;
            }
            else
            {
                risultato.Append(nome[i]);
                i++;
            }
        }
        return risultato.ToString();
    }

    // Inizio e fine di una parola, anche con CamelCase o cifre: servono a trovare le parti; se non sono
    // delimitate da separatori, SostituisciParti rinuncia (vedi sopra).
    private static bool InizioParola(string nome, int i)
    {
        if (i == 0 || !char.IsLetter(nome[i - 1])) return true;
        if (!char.IsUpper(nome[i])) return false;
        return char.IsLower(nome[i - 1])
               || (char.IsUpper(nome[i - 1]) && i + 1 < nome.Length && char.IsLower(nome[i + 1]));
    }

    private static bool FineParola(string nome, int fine) =>
        fine >= nome.Length || !char.IsLetter(nome[fine]) || char.IsUpper(nome[fine]);

    /// <summary>Rimette le parentesi o gli apici come nell'originale.</summary>
    private static string Riquota(string nome, QuoteType apici) => apici switch
    {
        QuoteType.SquareBracket => "[" + nome.Replace("]", "]]") + "]",
        QuoteType.DoubleQuote => "\"" + nome.Replace("\"", "\"\"") + "\"",
        _ => nome,
    };
}
