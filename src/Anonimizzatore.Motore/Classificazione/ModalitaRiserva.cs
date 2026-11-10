using Anonimizzatore.Motore.Lettura;
using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Modalità di riserva, per uno script con errori di sintassi (o scritto con una sintassi che il parser non conosce):
/// niente albero, si guardano solo i token. Si riconoscono:
/// <list type="bullet">
/// <item>i nomi che il contesto rivela: l'identificatore dopo <c>FROM</c>, <c>JOIN</c>, <c>INTO</c>, <c>UPDATE</c>, <c>TABLE</c>,
/// <c>VIEW</c>, <c>PROCEDURE</c>, <c>EXEC</c>, <c>FUNCTION</c>, <c>TRIGGER</c>, <c>SCHEMA</c>, <c>DATABASE</c>, <c>USE</c>
/// (con schema, database e server davanti);</item>
/// <item>i nomi già in mappa (da altri script o da sessioni precedenti), ovunque compaiano;</item>
/// <item>le parole della blacklist (come colonne o variabili);</item>
/// <item>le password e i SID (<c>PASSWORD = '...'</c>, <c>SID = 0x...</c>).</item>
/// </list>
/// Il resto (colonne mai viste, alias...) resta in chiaro: per questo il risultato porta sempre un avviso.
/// Il risultato è nello stesso formato della classificazione normale, quindi segue le stesse regole di decisione.
/// </summary>
public static class ModalitaRiserva
{
    private static readonly Dictionary<TSqlTokenType, Ruolo> Contesti = new()
    {
        [TSqlTokenType.From] = Ruolo.Tabella,
        [TSqlTokenType.Join] = Ruolo.Tabella,
        [TSqlTokenType.Into] = Ruolo.Tabella,
        [TSqlTokenType.Update] = Ruolo.Tabella,
        [TSqlTokenType.Table] = Ruolo.Tabella,
        [TSqlTokenType.References] = Ruolo.Tabella,
        [TSqlTokenType.Merge] = Ruolo.Tabella,
        [TSqlTokenType.View] = Ruolo.Vista,
        [TSqlTokenType.Procedure] = Ruolo.Procedura,
        [TSqlTokenType.Proc] = Ruolo.Procedura,
        [TSqlTokenType.Exec] = Ruolo.Procedura,
        [TSqlTokenType.Execute] = Ruolo.Procedura,
        [TSqlTokenType.Function] = Ruolo.Funzione,
        [TSqlTokenType.Trigger] = Ruolo.Trigger,
        [TSqlTokenType.Schema] = Ruolo.Schema,
        [TSqlTokenType.Database] = Ruolo.Database,
        [TSqlTokenType.Use] = Ruolo.Database,
    };

    // Con quale ruolo si riconosce un nome già in mappa quando ne esiste più d'uno con lo stesso nome (database e schema
    // "Vendite"): prima gli oggetti, poi il resto.
    private static readonly Ruolo[] PrioritaRuoli =
    [
        Ruolo.Tabella, Ruolo.Vista, Ruolo.Procedura, Ruolo.Funzione, Ruolo.Trigger, Ruolo.Schema, Ruolo.Database,
        Ruolo.LinkedServer, Ruolo.Server, Ruolo.Job, Ruolo.Colonna, Ruolo.Alias, Ruolo.Login, Ruolo.Utente, Ruolo.RuoloDb,
    ];

    public static RisultatoClassificazione Classifica(
        ScriptLetto letto, Conoscenza conoscenza, MappaCliente mappa, Func<string, bool> inBlacklist)
    {
        var token = letto.Token;
        var noti = NomiNoti(mappa);
        var nomi = new Dictionary<int, NomeTrovato>();
        var segreti = new Dictionary<int, Segreto>();

        // Indici dei token che contano (senza spazi e commenti).
        var sig = Enumerable.Range(0, token.Count).Where(i => !Ignorabile(token[i])).ToList();

        for (var k = 0; k < sig.Count; k++)
        {
            var idx = sig[k];
            var t = token[idx];

            // Password e SID: PASSWORD = '...', SID = 0x...
            if (k + 2 < sig.Count && token[sig[k + 1]].TokenType == TSqlTokenType.EqualsSign)
            {
                var valore = sig[k + 2];
                var parola = t.Text;
                var eStringa = token[valore].TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral;
                var eEsadecimale = token[valore].TokenType == TSqlTokenType.HexLiteral;
                if (parola.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) || parola.Equals("OLD_PASSWORD", StringComparison.OrdinalIgnoreCase)
                    || parola.Equals("PWD", StringComparison.OrdinalIgnoreCase))
                {
                    if (eStringa || eEsadecimale) segreti[valore] = new Segreto(valore, TipoSegreto.Password);
                }
                else if (parola.Equals("SID", StringComparison.OrdinalIgnoreCase) && eEsadecimale)
                {
                    segreti[valore] = new Segreto(valore, TipoSegreto.Sid);
                }
            }

            if (t.TokenType == TSqlTokenType.Variable)
            {
                if (t.Text.StartsWith("@@", StringComparison.Ordinal)) continue;
                if (noti.TryGetValue(t.Text, out var voceVar) && voceVar.Ruolo is Ruolo.Variabile or Ruolo.Parametro)
                    nomi.TryAdd(idx, new NomeTrovato(idx, voceVar.Ruolo, t.Text, QuoteType.NotQuoted, false));
                else if (inBlacklist(t.Text))
                    nomi.TryAdd(idx, new NomeTrovato(idx, Ruolo.Variabile, t.Text, QuoteType.NotQuoted, false));
                continue;
            }

            if (!EParte(t)) continue;

            // Un nome a più parti: a.b.c.d (una variabile SQLCMD $(Db) ne fa parte ma non è un nome da cambiare)
            var parti = new List<int> { idx };
            var j = k;
            while (j + 2 < sig.Count && token[sig[j + 1]].TokenType == TSqlTokenType.Dot && EParte(token[sig[j + 2]]))
            {
                j += 2;
                parti.Add(sig[j]);
            }

            var precedente = k > 0 ? token[sig[k - 1]].TokenType : TSqlTokenType.EndOfFile;
            if (Contesti.TryGetValue(precedente, out var ruoloUltima))
                ClassificaNomeDiOggetto(token, parti, ruoloUltima, conoscenza, nomi);
            else
                foreach (var p in parti) ClassificaNomeNoto(token[p], p, noti, inBlacklist, nomi);

            k = j;
        }

        return new RisultatoClassificazione([.. nomi.Values.OrderBy(n => n.IndiceToken)], [], [.. segreti.Values.OrderBy(s => s.IndiceToken)], []);
    }

    private static void ClassificaNomeDiOggetto(
        IList<TSqlParserToken> token, List<int> parti, Ruolo ruoloUltima, Conoscenza conoscenza, Dictionary<int, NomeTrovato> nomi)
    {
        // Da destra: l'ultima parte ha il ruolo del contesto, poi schema, database, server.
        var ruoli = ruoloUltima is Ruolo.Schema or Ruolo.Database
            ? new[] { ruoloUltima }
            : new[] { ruoloUltima, Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer };

        for (var i = 0; i < parti.Count && i < ruoli.Length; i++)
        {
            var indice = parti[^(i + 1)];
            if (EVariabileSqlcmd(token[indice])) continue;
            var nome = NomeSql.Normalizza(token[indice].Text);
            var ruolo = ruoli[i];
            if (i == 0)
            {
                if (nome.StartsWith('#')) ruolo = Ruolo.Temporanea;
                else if (ruolo == Ruolo.Tabella) ruolo = conoscenza.RuoloComeTabella(nome);
            }

            var schema = i == 0 && parti.Count >= 2 ? NomeSql.Normalizza(token[parti[^2]].Text) : null;
            var sistema = ruolo switch
            {
                Ruolo.Schema => ListeBase.SchemaPredefinito(nome),
                Ruolo.Database => ListeBase.DatabaseDiSistema(nome),
                _ => ListeBase.OggettoDiSistema(ruolo, nome, schema),
            };
            nomi.TryAdd(indice, new NomeTrovato(indice, ruolo, nome, Apici(token[indice].Text), sistema));
        }
    }

    private static void ClassificaNomeNoto(
        TSqlParserToken t, int indice, IReadOnlyDictionary<string, VoceMappa> noti, Func<string, bool> inBlacklist,
        Dictionary<int, NomeTrovato> nomi)
    {
        if (EVariabileSqlcmd(t)) return;
        var nome = NomeSql.Normalizza(t.Text);
        if (noti.TryGetValue(nome, out var voce))
            nomi.TryAdd(indice, new NomeTrovato(indice, voce.Ruolo, nome, Apici(t.Text), false));
        else if (inBlacklist(nome))
            nomi.TryAdd(indice, new NomeTrovato(indice, nome.StartsWith('#') ? Ruolo.Temporanea : Ruolo.Colonna, nome, Apici(t.Text), false));
    }

    private static Dictionary<string, VoceMappa> NomiNoti(MappaCliente mappa)
    {
        var noti = new Dictionary<string, VoceMappa>(NomeSql.Confronto);
        foreach (var ruolo in PrioritaRuoli)
            foreach (var voce in mappa.Voci.Where(v => v.Ruolo == ruolo)) noti.TryAdd(voce.Originale, voce);
        foreach (var voce in mappa.Voci.Where(v => v.Ruolo is Ruolo.Variabile or Ruolo.Parametro or Ruolo.Temporanea)) noti.TryAdd(voce.Originale, voce);
        return noti;
    }

    private static bool Ignorabile(TSqlParserToken t) =>
        t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
            or TSqlTokenType.EndOfFile;

    // Una parte di un nome: un identificatore, anche tra parentesi, o una variabile SQLCMD ($(DbName)).
    private static bool EParte(TSqlParserToken t) =>
        t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier or TSqlTokenType.SqlCommandIdentifier;

    // I nomi con una variabile SQLCMD non sono nomi: il valore vero sta nella riga :setvar.
    private static bool EVariabileSqlcmd(TSqlParserToken t) =>
        t.TokenType == TSqlTokenType.SqlCommandIdentifier || t.Text.Contains("$(", StringComparison.Ordinal);

    private static QuoteType Apici(string testo) =>
        testo.StartsWith('[') ? QuoteType.SquareBracket : testo.StartsWith('"') ? QuoteType.DoubleQuote : QuoteType.NotQuoted;
}
