using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>Un nome trovato nello script, con il ruolo che gli è stato dato.</summary>
/// <param name="IndiceToken">Posizione del token da sostituire nel testo originale.</param>
/// <param name="Ruolo">Che cosa rappresenta (tabella, colonna, parametro...).</param>
/// <param name="Nome">Il nome senza parentesi (le variabili con la @).</param>
/// <param name="Apici">Come era scritto: nudo, tra [parentesi] o tra "doppi apici".</param>
/// <param name="DiSistema">Vero se è un oggetto di sistema: non va mai anonimizzato.</param>
public sealed record NomeTrovato(int IndiceToken, Ruolo Ruolo, string Nome, QuoteType Apici, bool DiSistema);

/// <summary>Un'osservazione per l'utente (non blocca il lavoro).</summary>
public sealed record Avviso(string Messaggio, int Riga = 0);

/// <summary>
/// Passo 2 del motore: percorre l'albero sintattico e dà un <see cref="Ruolo"/> a ogni nome: oggetti (server,
/// database, schema, tabelle, viste, procedure, funzioni, trigger, temporanee), colonne, variabili e parametri,
/// alias di tabella, indici e constraint. Non cambia il testo e non decide cosa anonimizzare.
/// </summary>
public sealed class Classificatore : TSqlFragmentVisitor
{
    private static readonly HashSet<string> NomiPseudoTabella = new(NomeSql.Confronto) { "inserted", "deleted" };

    private readonly Conoscenza _conoscenza;
    private readonly PreAnalisi _pre;
    private readonly Dictionary<int, NomeTrovato> _trovati = [];
    private readonly Dictionary<SchemaObjectName, Ruolo> _previsti = [];
    private readonly HashSet<SchemaObjectName> _ignorati = [];
    private readonly HashSet<VariableReference> _variabiliDiSistema = [];
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

    /// <summary>Le variabili si chiamano sempre con la @ davanti, comunque le restituisca ScriptDom.</summary>
    public static string NomeVariabile(string nome) => nome.StartsWith('@') ? nome : "@" + nome;

    /// <summary>Che tipo di constraint è: serve al prefisso se il nome intero va anonimizzato (<c>pk_001</c>, <c>fk_001</c>...).</summary>
    public static Ruolo RuoloVincolo(ConstraintDefinition vincolo) => vincolo switch
    {
        UniqueConstraintDefinition { IsPrimaryKey: true } => Ruolo.PrimaryKey,
        UniqueConstraintDefinition => Ruolo.Unique,
        ForeignKeyConstraintDefinition => Ruolo.ForeignKey,
        DefaultConstraintDefinition => Ruolo.Default,
        _ => Ruolo.Check,
    };

    // Visit(TSqlFragment) è chiamato per ogni nodo, prima dei suoi figli: il genitore può quindi dire che ruolo
    // hanno i nomi che contiene.
    public override void Visit(TSqlFragment nodo)
    {
        // Alias di tabella. Se coincide con un oggetto vero (FROM dbo.Clienti AS Clienti) va sostituito insieme
        // all'oggetto; altrimenti è un alias.
        if (nodo is TableReferenceWithAlias conAlias && conAlias.Alias is { } alias)
            Aggiungi(alias, ENomeDiOggetto(alias.Value) ? _conoscenza.RuoloComeTabella(alias.Value) : Ruolo.Alias, sistema: false);

        switch (nodo)
        {
            // --- dichiarazioni di oggetti ---
            case CreateTableStatement n: Preved(n.SchemaObjectName, Ruolo.Tabella); break;
            case ViewStatementBody n:
                Preved(n.SchemaObjectName, Ruolo.Vista);
                Colonne(n.Columns);
                break;
            case ProcedureStatementBody n: Preved(n.ProcedureReference?.Name, Ruolo.Procedura); break;
            case FunctionStatementBody n: Preved(n.Name, Ruolo.Funzione); break;
            case TriggerStatementBody n: Preved(n.Name, Ruolo.Trigger); break;
            case CreateSchemaStatement n: Aggiungi(n.Name, Ruolo.Schema, ListeBase.SchemaPredefinito(n.Name.Value)); break;
            case CreateDatabaseStatement n: AggiungiDatabase(n.DatabaseName); break;
            case UseStatement n: AggiungiDatabase(n.DatabaseName); break;
            case DropObjectsStatement n: PrevediDrop(n); break;

            // --- riferimenti a oggetti ---
            case ExecutableProcedureReference n: ChiamataProcedura(n); break;
            case SchemaObjectFunctionTableReference n: Preved(n.SchemaObject, Ruolo.Funzione); break;
            case OpenQueryTableReference n when n.LinkedServer is not null:
                Aggiungi(n.LinkedServer, Ruolo.LinkedServer, sistema: false);
                break;
            case NamedTableReference n when ELocale(n.SchemaObject):
                // FROM Recenti (CTE) o UPDATE o (alias): non è un oggetto.
                _ignorati.Add(n.SchemaObject);
                Aggiungi(n.SchemaObject.BaseIdentifier, Ruolo.Alias, NomiPseudoTabella.Contains(n.SchemaObject.BaseIdentifier.Value));
                break;
            case FunctionCall n: ChiamataFunzione(n); break;

            // --- colonne ---
            case ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier: { Identifiers.Count: > 0 } mp }:
                Qualificatori([.. mp.Identifiers.SkipLast(1)]);
                Aggiungi(mp.Identifiers[^1], Ruolo.Colonna, sistema: false);
                break;
            case SelectStarExpression { Qualifier: not null } n:
                Qualificatori([.. n.Qualifier.Identifiers]);
                break;
            case ColumnDefinition n when n.ColumnIdentifier is not null:
                Aggiungi(n.ColumnIdentifier, Ruolo.Colonna, sistema: false);
                break;
            case AlterTableAlterColumnStatement n when n.ColumnIdentifier is not null:
                Aggiungi(n.ColumnIdentifier, Ruolo.Colonna, sistema: false);
                break;
            case AlterTableDropTableElementStatement n:
                foreach (var elemento in n.AlterTableDropTableElements.Where(e => e.Name is not null))
                    if (elemento.TableElementType == TableElementType.Column)
                        Aggiungi(elemento.Name, Ruolo.Colonna, sistema: false);
                    else
                        Aggiungi(elemento.Name, _conoscenza.RuoloVincolo(elemento.Name.Value) ?? Ruolo.Check, sistema: false);
                break;
            case SelectScalarExpression { ColumnName.Identifier: { } nomeColonna }:
                Aggiungi(nomeColonna, Ruolo.Colonna, sistema: false); // alias di colonna: si usa come una colonna
                break;
            case CommonTableExpression n:
                if (n.ExpressionName is not null) Aggiungi(n.ExpressionName, Ruolo.Alias, sistema: false);
                Colonne(n.Columns);
                break;
            case QueryDerivedTable n: Colonne(n.Columns); break;
            case InlineDerivedTable n: Colonne(n.Columns); break;

            // --- indici e constraint ---
            case ConstraintDefinition n:
                if (n.ConstraintIdentifier is not null) Aggiungi(n.ConstraintIdentifier, RuoloVincolo(n), sistema: false);
                if (n is ForeignKeyConstraintDefinition fk)
                {
                    Colonne(fk.Columns);
                    Colonne(fk.ReferencedTableColumns);
                }
                break;
            case CreateIndexStatement n when n.Name is not null: Aggiungi(n.Name, Ruolo.Indice, sistema: false); break;
            case IndexDefinition n when n.Name is not null: Aggiungi(n.Name, Ruolo.Indice, sistema: false); break;
            case AlterIndexStatement n when n.Name is not null: Aggiungi(n.Name, Ruolo.Indice, sistema: false); break;
            case DropIndexClause n when n.Index is not null: Aggiungi(n.Index, Ruolo.Indice, sistema: false); break;

            // --- variabili e parametri ---
            case ProcedureParameter n when n.VariableName is not null: AggiungiVariabile(n.VariableName, Ruolo.Parametro); break;
            case DeclareVariableElement n when n.VariableName is not null: AggiungiVariabile(n.VariableName, Ruolo.Variabile); break;
            case DeclareTableVariableBody n when n.VariableName is not null: AggiungiVariabile(n.VariableName, Ruolo.Variabile); break;
            case VariableReference n when !_variabiliDiSistema.Contains(n) && n.FirstTokenIndex >= 0:
                var nomeVariabile = NomeVariabile(n.Name);
                _trovati.TryAdd(n.FirstTokenIndex, new NomeTrovato(n.FirstTokenIndex,
                    _pre.Parametri.Contains(nomeVariabile) ? Ruolo.Parametro : Ruolo.Variabile,
                    nomeVariabile, QuoteType.NotQuoted, DiSistema: false));
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

    private void ChiamataProcedura(ExecutableProcedureReference chiamata)
    {
        var nome = chiamata.ProcedureReference?.ProcedureReference?.Name;
        Preved(nome, Ruolo.Procedura);

        // I parametri con nome di una procedura di sistema (@job_name = ...) sono di SQL Server, non del cliente.
        if (nome?.BaseIdentifier is { } id
            && ListeBase.OggettoDiSistema(Ruolo.Procedura, id.Value, nome.SchemaIdentifier?.Value))
            foreach (var p in chiamata.Parameters.Where(p => p.Variable is not null))
                _variabiliDiSistema.Add(p.Variable);
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
        if (prefissi.Count == 1 && ultimo.Value.StartsWith('@')) return; // variabile tabella

        if (prefissi.Count == 1 && ELocale(ultimo.Value)) // alias, CTE, inserted/deleted
        {
            Aggiungi(ultimo, Ruolo.Alias, NomiPseudoTabella.Contains(ultimo.Value));
            return;
        }

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

    private void Colonne(IEnumerable<Identifier>? nomi)
    {
        if (nomi is null) return;
        foreach (var nome in nomi) Aggiungi(nome, Ruolo.Colonna, sistema: false);
    }

    private void AggiungiVariabile(Identifier id, Ruolo ruolo)
    {
        if (id.FirstTokenIndex < 0) return;
        var nome = NomeVariabile(id.Value);
        _trovati.TryAdd(id.FirstTokenIndex, new NomeTrovato(id.FirstTokenIndex, ruolo, nome, QuoteType.NotQuoted, DiSistema: false));
    }

    private void AggiungiDatabase(Identifier id) => Aggiungi(id, Ruolo.Database, ListeBase.DatabaseDiSistema(id.Value));

    private void Aggiungi(Identifier? id, Ruolo ruolo, bool sistema)
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
