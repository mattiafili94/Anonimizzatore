using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Primo giro sull'albero di uno script, prima di classificare: raccoglie i <c>CREATE</c> e i constraint (alimentano
/// la <see cref="Conoscenza"/>), i tipi di colonne e variabili, i parametri, gli alias e i nomi delle CTE.
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

    /// <summary>Nomi (con la @) dichiarati come parametri di procedure e funzioni: gli altri sono variabili.</summary>
    public HashSet<string> Parametri { get; } = new(NomeSql.Confronto);

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

            // --- oggetti dichiarati ---
            case CreateTableStatement n: Dichiara(n.SchemaObjectName, Ruolo.Tabella); break;
            case ViewStatementBody n: Dichiara(n.SchemaObjectName, Ruolo.Vista); break;
            case ProcedureStatementBody n: Dichiara(n.ProcedureReference?.Name, Ruolo.Procedura); break;
            case FunctionStatementBody n: Dichiara(n.Name, Ruolo.Funzione); break;
            case TriggerStatementBody n: Dichiara(n.Name, Ruolo.Trigger); break;

            // --- constraint e indici ---
            case ConstraintDefinition n when n.ConstraintIdentifier is not null:
                _conoscenza.DichiaraVincolo(n.ConstraintIdentifier.Value, Classificatore.RuoloVincolo(n));
                break;
            case CreateIndexStatement n when n.Name is not null:
                _conoscenza.DichiaraVincolo(n.Name.Value, Ruolo.Indice);
                break;
            case IndexDefinition n when n.Name is not null:
                _conoscenza.DichiaraVincolo(n.Name.Value, Ruolo.Indice);
                break;

            // --- tipi dichiarati ---
            case ColumnDefinition n when n.ColumnIdentifier is not null:
                _conoscenza.DichiaraTipo(Ruolo.Colonna, n.ColumnIdentifier.Value, TipiSql.Formatta(n.DataType));
                break;
            case AlterTableAlterColumnStatement n when n.ColumnIdentifier is not null:
                _conoscenza.DichiaraTipo(Ruolo.Colonna, n.ColumnIdentifier.Value, TipiSql.Formatta(n.DataType));
                break;
            case ProcedureParameter n when n.VariableName is not null:
                var parametro = Classificatore.NomeVariabile(n.VariableName.Value);
                Parametri.Add(parametro);
                _conoscenza.DichiaraTipo(Ruolo.Parametro, parametro, TipiSql.Formatta(n.DataType));
                break;
            case DeclareVariableElement n when n.VariableName is not null:
                _conoscenza.DichiaraTipo(Ruolo.Variabile, Classificatore.NomeVariabile(n.VariableName.Value),
                    TipiSql.Formatta(n.DataType));
                break;

            // --- tipi dedotti da CAST / CONVERT ---
            case CastCall n: Deduci(n.Parameter, n.DataType); break;
            case TryCastCall n: Deduci(n.Parameter, n.DataType); break;
            case ConvertCall n: Deduci(n.Parameter, n.DataType); break;
            case TryConvertCall n: Deduci(n.Parameter, n.DataType); break;
        }
    }

    private void Dichiara(SchemaObjectName? nome, Ruolo ruolo)
    {
        if (nome?.BaseIdentifier is { } id) _conoscenza.Dichiara(id.Value, ruolo);
    }

    private void Deduci(ScalarExpression? espressione, DataTypeReference? tipo)
    {
        var tipoSql = TipiSql.Formatta(tipo);
        switch (espressione)
        {
            case ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier: { Identifiers.Count: > 0 } mp }:
                _conoscenza.DichiaraTipo(Ruolo.Colonna, mp.Identifiers[^1].Value, tipoSql, dedotto: true);
                break;
            case VariableReference v:
                _conoscenza.DichiaraTipo(Ruolo.Variabile, Classificatore.NomeVariabile(v.Name), tipoSql, dedotto: true);
                break;
        }
    }
}
