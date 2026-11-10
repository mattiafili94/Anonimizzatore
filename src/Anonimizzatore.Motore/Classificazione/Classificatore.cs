using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>Un nome trovato nello script, con il ruolo che gli è stato dato.</summary>
/// <param name="IndiceToken">Posizione del token da sostituire nel testo originale.</param>
/// <param name="Ruolo">Che cosa rappresenta (tabella, schema, procedura...).</param>
/// <param name="Nome">Il nome senza parentesi.</param>
/// <param name="Apici">Come era scritto: nudo, tra [parentesi] o tra "doppi apici".</param>
/// <param name="DiSistema">Vero se è un oggetto di sistema: non va mai anonimizzato.</param>
public sealed record NomeTrovato(int IndiceToken, Ruolo Ruolo, string Nome, QuoteType Apici, bool DiSistema);

/// <summary>Un'osservazione per l'utente (non blocca il lavoro).</summary>
public sealed record Avviso(string Messaggio, int Riga = 0);

/// <summary>
/// Passo 2 del motore: percorre l'albero sintattico e dà un <see cref="Ruolo"/> a ogni nome di oggetto (livello 1:
/// server, database, schema, tabelle, viste, procedure, funzioni, trigger, temporanee). Non cambia il testo.
/// Colonne, variabili e constraint arrivano nella tappa 4.
/// </summary>
public sealed class Classificatore : TSqlFragmentVisitor
{
    private static readonly HashSet<string> NomiPseudoTabella = new(NomeSql.Confronto) { "inserted", "deleted" };

    private readonly Conoscenza _conoscenza;
    private readonly PreAnalisi _pre;
    private readonly Dictionary<int, NomeTrovato> _trovati = [];
    private readonly Dictionary<SchemaObjectName, Ruolo> _previsti = [];
    private readonly HashSet<SchemaObjectName> _ignorati = [];
    private readonly List<Avviso> _avvisi = [];

    private Classificatore(Conoscenza conoscenza, PreAnalisi pre)
    {
        _conoscenza = conoscenza;
        _pre = pre;
    }

    /// <summary>Classifica i nomi di uno script, in ordine di posizione nel testo.</summary>
    public static (IReadOnlyList<NomeTrovato> Nomi, IReadOnlyList<Avviso> Avvisi) Classifica(
        TSqlFragment albero, Conoscenza conoscenza, PreAnalisi pre)
    {
        var c = new Classificatore(conoscenza, pre);
        albero.Accept(c);
        return ([.. c._trovati.Values.OrderBy(n => n.IndiceToken)], c._avvisi);
    }

    // Visit(TSqlFragment) è chiamato per ogni nodo, prima dei suoi figli: il genitore può quindi dire che ruolo
    // hanno i nomi che contiene.
    public override void Visit(TSqlFragment nodo)
    {
        // Alias con lo stesso nome di un oggetto vero (FROM dbo.Clienti AS Clienti): va sostituito insieme all'oggetto.
        if (nodo is TableReferenceWithAlias conAlias && conAlias.Alias is { } alias && ENomeDiOggetto(alias.Value))
            Aggiungi(alias, _conoscenza.RuoloComeTabella(alias.Value), sistema: false);

        switch (nodo)
        {
            // --- dichiarazioni ---
            case CreateTableStatement n: Preved(n.SchemaObjectName, Ruolo.Tabella); break;
            case ViewStatementBody n: Preved(n.SchemaObjectName, Ruolo.Vista); break;
            case ProcedureStatementBody n: Preved(n.ProcedureReference?.Name, Ruolo.Procedura); break;
            case FunctionStatementBody n: Preved(n.Name, Ruolo.Funzione); break;
            case TriggerStatementBody n: Preved(n.Name, Ruolo.Trigger); break;
            case CreateSchemaStatement n: Aggiungi(n.Name, Ruolo.Schema, ListeBase.SchemaPredefinito(n.Name.Value)); break;
            case CreateDatabaseStatement n: AggiungiDatabase(n.DatabaseName); break;
            case UseStatement n: AggiungiDatabase(n.DatabaseName); break;
            case DropObjectsStatement n: PrevediDrop(n); break;

            // --- riferimenti ---
            case ExecutableProcedureReference n:
                Preved(n.ProcedureReference?.ProcedureReference?.Name, Ruolo.Procedura);
                break;
            case SchemaObjectFunctionTableReference n: Preved(n.SchemaObject, Ruolo.Funzione); break;
            case OpenQueryTableReference n when n.LinkedServer is not null:
                Aggiungi(n.LinkedServer, Ruolo.LinkedServer, sistema: false);
                break;
            case NamedTableReference n when ELocale(n.SchemaObject):
                _ignorati.Add(n.SchemaObject);
                break;
            case FunctionCall n: ChiamataFunzione(n); break;
            case ColumnReferenceExpression n when n.ColumnType == ColumnType.Regular && n.MultiPartIdentifier is not null:
                Qualificatori([.. n.MultiPartIdentifier.Identifiers.SkipLast(1)]);
                break;
            case SelectStarExpression n when n.Qualifier is not null:
                Qualificatori([.. n.Qualifier.Identifiers]);
                break;

            // --- tipi di dato: non sono oggetti del cliente ---
            case UserDataTypeReference n:
                _ignorati.Add(n.Name);
                _avvisi.Add(new Avviso($"Tipo definito dall'utente '{n.Name.BaseIdentifier.Value}' lasciato in chiaro.", n.StartLine));
                break;
            case DataTypeReference n when n.Name is not null:
                _ignorati.Add(n.Name);
                break;

            // --- ogni altro nome di oggetto: il ruolo previsto, oppure tabella/vista ---
            case SchemaObjectName n: ProcessaNome(n); break;
        }
    }

    private void Preved(SchemaObjectName? nome, Ruolo ruolo)
    {
        if (nome is not null) _previsti[nome] = ruolo;
    }

    private void PrevediDrop(DropObjectsStatement drop)
    {
        var ruolo = drop switch
        {
            DropTableStatement => Ruolo.Tabella,
            DropViewStatement => Ruolo.Vista,
            DropProcedureStatement => Ruolo.Procedura,
            DropFunctionStatement => Ruolo.Funzione,
            DropTriggerStatement => Ruolo.Trigger,
            _ => (Ruolo?)null,
        };
        if (ruolo is null) return;
        foreach (var oggetto in drop.Objects) Preved(oggetto, ruolo.Value);
    }

    private void ProcessaNome(SchemaObjectName nome)
    {
        if (_ignorati.Contains(nome) || nome.BaseIdentifier is null) return;

        var baseId = nome.BaseIdentifier;
        var schema = nome.SchemaIdentifier?.Value;

        if (nome.ServerIdentifier is { } server) Aggiungi(server, Ruolo.LinkedServer, sistema: false);
        if (nome.DatabaseIdentifier is { } db) AggiungiDatabase(db);
        if (nome.SchemaIdentifier is { } sch) Aggiungi(sch, Ruolo.Schema, ListeBase.SchemaPredefinito(sch.Value));

        Ruolo ruolo;
        if (baseId.Value.StartsWith('#')) ruolo = Ruolo.Temporanea;
        else if (_previsti.TryGetValue(nome, out var previsto)) ruolo = previsto;
        else ruolo = _conoscenza.RuoloComeTabella(baseId.Value);

        Aggiungi(baseId, ruolo, ListeBase.OggettoDiSistema(ruolo, baseId.Value, schema));
    }

    private void ChiamataFunzione(FunctionCall chiamata)
    {
        // Solo le chiamate con prefisso (dbo.fnSconto(...)): senza prefisso sono funzioni predefinite (GETDATE...).
        if (chiamata.CallTarget is not MultiPartIdentifierCallTarget target || chiamata.FunctionName is null) return;
        if (ListeBase.MetodoDiOggetto(chiamata.FunctionName.Value)) return;

        var prefissi = target.MultiPartIdentifier.Identifiers;
        var schema = prefissi[^1].Value;
        AggiungiPrefissi(prefissi, [Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer]);
        Aggiungi(chiamata.FunctionName, Ruolo.Funzione,
            ListeBase.OggettoDiSistema(Ruolo.Funzione, chiamata.FunctionName.Value, schema));
    }

    /// <summary>Prefissi di una colonna (<c>Ordini.IdOrdine</c>, <c>Vendite.dbo.Ordini.IdOrdine</c>) o di un <c>*</c>.</summary>
    private void Qualificatori(IList<Identifier> prefissi)
    {
        if (prefissi.Count == 0) return;

        var ultimo = prefissi[^1];
        if (prefissi.Count == 1 && (ultimo.Value.StartsWith('@') || ELocale(ultimo.Value))) return; // alias, CTE, inserted...

        var ruoloTabella = ultimo.Value.StartsWith('#') ? Ruolo.Temporanea : _conoscenza.RuoloComeTabella(ultimo.Value);
        var schema = prefissi.Count >= 2 ? prefissi[^2].Value : null;
        AggiungiPrefissi(prefissi, [ruoloTabella, Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer], schema);
    }

    /// <summary>Assegna i ruoli ai prefissi partendo da destra: l'ultimo ha il primo ruolo, il penultimo il secondo...</summary>
    private void AggiungiPrefissi(IList<Identifier> prefissi, Ruolo[] ruoliDaDestra, string? schemaPerSistema = null)
    {
        for (var i = 0; i < prefissi.Count && i < ruoliDaDestra.Length; i++)
        {
            var id = prefissi[^(i + 1)];
            var ruolo = ruoliDaDestra[i];
            var schema = i == 0 ? schemaPerSistema : null;
            var sistema = ruolo switch
            {
                Ruolo.Schema => ListeBase.SchemaPredefinito(id.Value),
                Ruolo.Database => ListeBase.DatabaseDiSistema(id.Value),
                _ => ListeBase.OggettoDiSistema(ruolo, id.Value, schema),
            };
            Aggiungi(id, ruolo, sistema);
        }
    }

    private void AggiungiDatabase(Identifier id) => Aggiungi(id, Ruolo.Database, ListeBase.DatabaseDiSistema(id.Value));

    private void Aggiungi(Identifier id, Ruolo ruolo, bool sistema)
    {
        if (id is null || id.FirstTokenIndex < 0) return;
        _trovati.TryAdd(id.FirstTokenIndex, new NomeTrovato(id.FirstTokenIndex, ruolo, id.Value, id.QuoteType, sistema));
    }

    /// <summary>Nome locale: alias, CTE o inserted/deleted. Se coincide con un oggetto vero non è considerato locale.</summary>
    private bool ELocale(string nome) =>
        (_pre.Alias.Contains(nome) || _pre.Cte.Contains(nome) || NomiPseudoTabella.Contains(nome))
        && !ENomeDiOggetto(nome);

    private bool ELocale(SchemaObjectName nome) =>
        nome.Identifiers.Count == 1 && nome.BaseIdentifier is { } id && ELocale(id.Value);

    private bool ENomeDiOggetto(string nome) => _pre.OggettiQualificati.Contains(nome) || _conoscenza.Contiene(nome);
}
