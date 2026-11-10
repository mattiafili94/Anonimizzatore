using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Primo giro sull'albero di uno script, prima di classificare: raccoglie i <c>CREATE</c> (alimentano la
/// <see cref="Conoscenza"/>), gli alias e i nomi delle CTE (nomi locali, che restano in chiaro).
/// </summary>
public sealed class PreAnalisi : TSqlFragmentVisitor
{
    private readonly Conoscenza _conoscenza;

    public PreAnalisi(Conoscenza conoscenza) => _conoscenza = conoscenza;

    /// <summary>Alias di tabella e di MERGE.</summary>
    public HashSet<string> Alias { get; } = new(NomeSql.Confronto);

    /// <summary>Nomi di CTE (<c>WITH nome AS (...)</c>).</summary>
    public HashSet<string> Cte { get; } = new(NomeSql.Confronto);

    /// <summary>Nomi finali di oggetti scritti con schema o database davanti (<c>dbo.Clienti</c>): sicuramente oggetti veri.</summary>
    public HashSet<string> OggettiQualificati { get; } = new(NomeSql.Confronto);

    public override void Visit(TSqlFragment nodo)
    {
        if (nodo is TableReferenceWithAlias conAlias && conAlias.Alias is not null)
            Alias.Add(conAlias.Alias.Value);

        switch (nodo)
        {
            case CommonTableExpression cte when cte.ExpressionName is not null:
                Cte.Add(cte.ExpressionName.Value);
                break;
            case MergeSpecification merge when merge.TableAlias is not null:
                Alias.Add(merge.TableAlias.Value);
                break;
            case NamedTableReference rif when rif.SchemaObject.Identifiers.Count >= 2:
                OggettiQualificati.Add(rif.SchemaObject.BaseIdentifier.Value);
                break;

            case CreateTableStatement n: Dichiara(n.SchemaObjectName, Ruolo.Tabella); break;
            case ViewStatementBody n: Dichiara(n.SchemaObjectName, Ruolo.Vista); break;
            case ProcedureStatementBody n: Dichiara(n.ProcedureReference?.Name, Ruolo.Procedura); break;
            case FunctionStatementBody n: Dichiara(n.Name, Ruolo.Funzione); break;
            case TriggerStatementBody n: Dichiara(n.Name, Ruolo.Trigger); break;
        }
    }

    private void Dichiara(SchemaObjectName? nome, Ruolo ruolo)
    {
        if (nome?.BaseIdentifier is { } id) _conoscenza.Dichiara(id.Value, ruolo);
    }
}
