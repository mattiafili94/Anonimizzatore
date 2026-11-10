using Anonimizzatore.Motore.Mappa;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>Un nome trovato nello script, con il ruolo che gli è stato dato.</summary>
/// <param name="IndiceToken">Posizione del token da sostituire nel testo originale.</param>
/// <param name="Ruolo">Che cosa rappresenta (tabella, colonna, parametro...).</param>
/// <param name="Nome">Il nome senza parentesi (le variabili con la @).</param>
/// <param name="Apici">Come era scritto: nudo, tra [parentesi] o tra "doppi apici".</param>
/// <param name="DiSistema">Vero se è un oggetto di sistema: non va mai anonimizzato.</param>
/// <param name="DaStringa">Vero se il nome sta dentro un letterale stringa (<c>EXECUTE AS USER = 'nome'</c>): il token da
/// sostituire è l'intera stringa e va riscritta con i suoi apici.</param>
public sealed record NomeTrovato(
    int IndiceToken, Ruolo Ruolo, string Nome, QuoteType Apici, bool DiSistema, bool DaStringa = false);

/// <summary>Un'osservazione per l'utente (non blocca il lavoro).</summary>
public sealed record Avviso(string Messaggio, int Riga = 0);

/// <summary>Che cosa nasconde un <see cref="Segreto"/>.</summary>
public enum TipoSegreto
{
    Password,
    Sid,
}

/// <summary>
/// Un valore che non deve mai arrivare all'IA (password, hash di password, SID) e che non viene salvato da nessuna
/// parte: il token viene sostituito da un segnaposto fisso. Non è un nome, quindi non entra nella mappa.
/// </summary>
public sealed record Segreto(int IndiceToken, TipoSegreto Tipo);

/// <summary>Tutto ciò che il classificatore trova in uno script.</summary>
/// <param name="Comandi">Posizioni delle stringhe che sono righe di comando (<c>xp_cmdshell</c>, passi di job CmdExec): vanno
/// lette come testo libero, dove uno spazio chiude un percorso e <c>/E</c> è un'opzione, non una cartella.</param>
public sealed record RisultatoClassificazione(
    IReadOnlyList<NomeTrovato> Nomi, IReadOnlyList<Avviso> Avvisi, IReadOnlyList<Segreto> Segreti, IReadOnlyList<int> Comandi);

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
    private readonly Dictionary<int, Segreto> _segreti = [];
    private readonly HashSet<int> _comandi = [];
    private Ruolo _ruoloNomeOpzione = Ruolo.Utente; // login o utente di cui si stanno leggendo le opzioni (WITH NAME = ...)

    private Classificatore(Conoscenza conoscenza, PreAnalisi pre)
    {
        _conoscenza = conoscenza;
        _pre = pre;
    }

    /// <summary>Classifica i nomi e i segreti di uno script, in ordine di posizione nel testo.</summary>
    public static RisultatoClassificazione Classifica(TSqlFragment albero, Conoscenza conoscenza, PreAnalisi pre)
    {
        var c = new Classificatore(conoscenza, pre);
        albero.Accept(c);
        return new RisultatoClassificazione(
            [.. c._trovati.Values.OrderBy(n => n.IndiceToken)], c._avvisi, [.. c._segreti.Values.OrderBy(s => s.IndiceToken)],
            [.. c._comandi.Order()]);
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
            case CreateSchemaStatement n:
                Aggiungi(n.Name, Ruolo.Schema, ListeBase.SchemaPredefinito(n.Name.Value));
                PrincipaleAmbiguo(n.Owner);
                break;
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

            // --- sicurezza: login, utenti, ruoli (livello 2) ---
            case CreateLoginStatement n: Principale(n.Name, Ruolo.Login); _ruoloNomeOpzione = Ruolo.Login; break;
            case AlterLoginStatement n: Principale(n.Name, Ruolo.Login); _ruoloNomeOpzione = Ruolo.Login; break;
            case UserStatement n: Principale(n.Name, Ruolo.Utente); _ruoloNomeOpzione = Ruolo.Utente; break;
            case UserLoginOption { UserLoginOptionType: UserLoginOptionType.Login, Identifier: not null } n:
                Principale(n.Identifier, Ruolo.Login);
                break;
            case CreateRoleStatement n: Principale(n.Name, Ruolo.RuoloDb); PrincipaleAmbiguo(n.Owner); break;
            case RoleStatement n: Principale(n.Name, Ruolo.RuoloDb); break;
            case ApplicationRoleStatement n: Principale(n.Name, Ruolo.RuoloDb); break;
            case ApplicationRoleOption { OptionKind: ApplicationRoleOptionKind.DefaultSchema, Value.Identifier: { } schema }:
                Aggiungi(schema, Ruolo.Schema, ListeBase.SchemaPredefinito(schema.Value));
                break;
            case ApplicationRoleOption { OptionKind: ApplicationRoleOptionKind.Password, Value.ValueExpression: Literal password }:
                NascondiSegreto(password, TipoSegreto.Password);
                break;
            case AddMemberAlterRoleAction n: PrincipaleAmbiguo(n.Member); break;
            case DropMemberAlterRoleAction n: PrincipaleAmbiguo(n.Member); break;
            case RenameAlterRoleAction n: Principale(n.NewName, Ruolo.RuoloDb); break;
            case DropUnownedObjectStatement n: PrevediDropPrincipale(n); break;
            case SecurityStatement n: Permessi(n); break;
            case AlterAuthorizationStatement n:
                Bersaglio(n.SecurityTargetObject);
                PrincipaleAmbiguo(n.PrincipalName);
                break;
            case ExecuteAsStatement { ExecuteContext: { Kind: ExecuteAsOption.Login or ExecuteAsOption.User, Principal: StringLiteral s } c }:
                Stringa(s, c.Kind == ExecuteAsOption.Login ? Ruolo.Login : Ruolo.Utente, ListeBase.PrincipaleDiSistema(s.Value));
                break;
            case ExecuteAsClause { Literal: StringLiteral s }:
                PrincipaleInStringa(s);
                break;
            case SetUserStatement { UserName: StringLiteral s }:
                PrincipaleInStringa(s);
                break;

            // --- backup, restore e file di database (livello 3) ---
            case BackupStatement { DatabaseName: not null } n: NomeDatabase(n.DatabaseName); break;
            case RestoreStatement { DatabaseName: not null } n: NomeDatabase(n.DatabaseName); break;
            case DeviceInfo { LogicalDevice: not null } n: NomeFile(n.LogicalDevice); break;
            case MoveRestoreOption { LogicalFileName: StringLiteral s }: Stringa(s, Ruolo.File, sistema: false); break;
            case NameFileDeclarationOption { LogicalFileName: not null } n: NomeFile(n.LogicalFileName); break;
            case AlterDatabaseStatement n:
                if (!n.UseCurrent && n.DatabaseName is not null) AggiungiDatabase(n.DatabaseName);
                if (n is AlterDatabaseModifyNameStatement { NewDatabaseName: not null } rinomina) AggiungiDatabase(rinomina.NewDatabaseName);
                break;
            case DropDatabaseStatement n:
                foreach (var database in n.Databases) AggiungiDatabase(database);
                break;
            case DbccStatement n: Dbcc(n); break;

            // --- segreti: password, hash di password, SID ---
            case PasswordCreateLoginSource n: NascondiSegreto(n.Password, TipoSegreto.Password); break;
            case PasswordAlterPrincipalOption n:
                NascondiSegreto(n.Password, TipoSegreto.Password);
                NascondiSegreto(n.OldPassword, TipoSegreto.Password);
                break;
            case LiteralPrincipalOption { OptionKind: PrincipalOptionKind.Sid } n: NascondiSegreto(n.Value, TipoSegreto.Sid); break;
            case LiteralPrincipalOption { OptionKind: PrincipalOptionKind.Password } n: NascondiSegreto(n.Value, TipoSegreto.Password); break;
            case IdentifierPrincipalOption n: OpzioneConIdentificatore(n); break;
            case MasterKeyStatement n: NascondiSegreto(n.Password, TipoSegreto.Password); break;
            case CertificateStatementBase n:
                NascondiSegreto(n.EncryptionPassword, TipoSegreto.Password);
                NascondiSegreto(n.DecryptionPassword, TipoSegreto.Password);
                break;
            case CryptoMechanism { CryptoMechanismType: CryptoMechanismType.Password } n:
                NascondiSegreto(n.PasswordOrSignature, TipoSegreto.Password);
                break;
            case CredentialStatement n:
                NascondiSegreto(n.Secret, TipoSegreto.Password);
                if (n.Identity is StringLiteral { Value: not "SHARED ACCESS SIGNATURE" and not "Managed Identity" } identita)
                    PrincipaleInStringa(identita);
                break;

            // --- tipi di dato: non sono oggetti del cliente ---
            case UserDataTypeReference n:
                _ignorati.Add(n.Name);
                if (!n.Name.BaseIdentifier.Value.Equals("sysname", StringComparison.OrdinalIgnoreCase)) // sysname è di sistema
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

        if (nome?.BaseIdentifier is not { } id
            || !ListeBase.OggettoDiSistema(Ruolo.Procedura, id.Value, nome.SchemaIdentifier?.Value))
            return;

        // I parametri con nome di una procedura di sistema (@job_name = ...) sono di SQL Server, non del cliente.
        // Fa eccezione sp_executesql: @stmt e @params sono suoi, ma gli altri (@p = 1) sono i parametri del SQL
        // dinamico e hanno lo stesso nome delle variabili dentro la stringa.
        var eSpExecuteSql = id.Value.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase);
        foreach (var p in chiamata.Parameters.Where(p => p.Variable is not null))
            if (!eSpExecuteSql || p.Variable.Name is "@stmt" or "@params")
                _variabiliDiSistema.Add(p.Variable);

        // Le procedure di sistema per login, utenti e ruoli ricevono nomi e password come stringhe.
        if (ProcedureSicurezza.Firma(id.Value) is { } firma) ArgomentiSicurezza(chiamata, firma);

        SegnaComandi(chiamata, id.Value);
    }

    /// <summary>Le stringhe che sono righe di comando: <c>xp_cmdshell '...'</c> e il <c>@command</c> di un passo di job non T-SQL.</summary>
    private void SegnaComandi(ExecutableProcedureReference chiamata, string procedura)
    {
        if (procedura.Equals("xp_cmdshell", StringComparison.OrdinalIgnoreCase))
        {
            SegnaComando(ValoreParametro(chiamata, "@command_string", 0));
        }
        else if (procedura.Equals("sp_add_jobstep", StringComparison.OrdinalIgnoreCase)
                 || procedura.Equals("sp_update_jobstep", StringComparison.OrdinalIgnoreCase))
        {
            // Senza @subsystem il passo è T-SQL; CmdExec, PowerShell... sono righe di comando.
            if (ValoreParametro(chiamata, "@subsystem", 4) is StringLiteral sottosistema
                && !sottosistema.Value.Equals("TSQL", StringComparison.OrdinalIgnoreCase))
                SegnaComando(ValoreParametro(chiamata, "@command", 5));
        }
    }

    private static ScalarExpression? ValoreParametro(ExecutableProcedureReference chiamata, string nome, int posizione)
    {
        var conNome = chiamata.Parameters.FirstOrDefault(p => p.Variable?.Name.Equals(nome, StringComparison.OrdinalIgnoreCase) == true);
        if (conNome is not null) return conNome.ParameterValue;
        return posizione < chiamata.Parameters.Count && chiamata.Parameters[posizione].Variable is null
            ? chiamata.Parameters[posizione].ParameterValue
            : null;
    }

    private void SegnaComando(ScalarExpression? valore)
    {
        if (valore is StringLiteral { FirstTokenIndex: >= 0 } s) _comandi.Add(s.FirstTokenIndex);
    }

    private void ArgomentiSicurezza(ExecutableProcedureReference chiamata, IReadOnlyList<(string Nome, ArgSicurezza? Tipo)> firma)
    {
        for (var i = 0; i < chiamata.Parameters.Count; i++)
        {
            var parametro = chiamata.Parameters[i];
            var tipo = parametro.Variable is { } conNome
                ? firma.FirstOrDefault(f => f.Nome.Equals(conNome.Name, StringComparison.OrdinalIgnoreCase)).Tipo
                : i < firma.Count ? firma[i].Tipo : null;
            if (tipo is null || parametro.ParameterValue is not StringLiteral valore) continue;

            switch (tipo.Value)
            {
                case ArgSicurezza.Login: Stringa(valore, Ruolo.Login, ListeBase.PrincipaleDiSistema(valore.Value)); break;
                case ArgSicurezza.Utente: Stringa(valore, Ruolo.Utente, ListeBase.PrincipaleDiSistema(valore.Value)); break;
                case ArgSicurezza.Ruolo: Stringa(valore, Ruolo.RuoloDb, ListeBase.PrincipaleDiSistema(valore.Value)); break;
                case ArgSicurezza.Principale: PrincipaleInStringa(valore); break;
                case ArgSicurezza.Database: Stringa(valore, Ruolo.Database, ListeBase.DatabaseDiSistema(valore.Value)); break;
                case ArgSicurezza.ServerCollegato: Stringa(valore, Ruolo.LinkedServer, false); break;
                case ArgSicurezza.Host: Stringa(valore, Ruolo.Server, ListeBase.ServerLocale(valore.Value)); break;
                case ArgSicurezza.Job: Stringa(valore, Ruolo.Job, false); break;
                case ArgSicurezza.Password: NascondiSegreto(valore, TipoSegreto.Password); break;
                case ArgSicurezza.Sid: NascondiSegreto(valore, TipoSegreto.Sid); break;
            }
        }
    }

    // --- backup, restore e file di database ---

    /// <summary>Nome di un database in <c>BACKUP DATABASE x</c> / <c>RESTORE DATABASE x</c>: un identificatore, una stringa o una variabile.</summary>
    private void NomeDatabase(IdentifierOrValueExpression nome)
    {
        if (nome.Identifier is { } id) AggiungiDatabase(id);
        else if (nome.ValueExpression is StringLiteral s) Stringa(s, Ruolo.Database, ListeBase.DatabaseDiSistema(s.Value));
    }

    /// <summary>Nome logico di un file o di un dispositivo di backup (<c>NAME = Vendite_Data</c>, <c>TO BackupVendite</c>).</summary>
    private void NomeFile(IdentifierOrValueExpression nome)
    {
        if (nome.Identifier is { } id) Aggiungi(id, Ruolo.File, sistema: false);
        else if (nome.ValueExpression is StringLiteral s) Stringa(s, Ruolo.File, sistema: false);
    }

    /// <summary>
    /// Il primo argomento di <c>DBCC CHECKDB('nome')</c>, <c>SHRINKDATABASE</c>, <c>SHRINKFILE</c>... è un database o un file.
    /// Scritto senza apici è letto come colonna: la classificazione fatta qui, prima dei figli, ha la precedenza.
    /// </summary>
    private void Dbcc(DbccStatement comando)
    {
        var ruolo = comando.Command switch
        {
            DbccCommand.CheckDB or DbccCommand.CheckAlloc or DbccCommand.CheckCatalog or DbccCommand.CheckFileGroup
                or DbccCommand.ShrinkDatabase or DbccCommand.UpdateUsage => Ruolo.Database,
            DbccCommand.ShrinkFile => Ruolo.File,
            _ => (Ruolo?)null,
        };
        if (ruolo is null || comando.Literals.Count == 0) return;

        var sistema = ruolo == Ruolo.Database;
        switch (comando.Literals[0].Value)
        {
            case StringLiteral s:
                Stringa(s, ruolo.Value, sistema && ListeBase.DatabaseDiSistema(s.Value));
                break;
            case IdentifierLiteral { FirstTokenIndex: >= 0 } nudo: // DBCC CHECKDB (Vendite): senza apici
                _trovati.TryAdd(nudo.FirstTokenIndex, new NomeTrovato(nudo.FirstTokenIndex, ruolo.Value, nudo.Value,
                    nudo.QuoteType, sistema && ListeBase.DatabaseDiSistema(nudo.Value)));
                break;
            case ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [var id] }:
                Aggiungi(id, ruolo.Value, sistema && ListeBase.DatabaseDiSistema(id.Value));
                break;
        }
    }

    // --- sicurezza ---

    private void Principale(Identifier? id, Ruolo ruolo)
    {
        if (id is not null) Aggiungi(id, ruolo, ListeBase.PrincipaleDiSistema(id.Value));
    }

    /// <summary>Un nome che può essere un utente o un ruolo (<c>GRANT ... TO nome</c>): ruolo se è noto come tale.</summary>
    private void PrincipaleAmbiguo(Identifier? id)
    {
        if (id is not null) Principale(id, _conoscenza.RuoloPrincipale(id.Value));
    }

    private void PrincipaleInStringa(StringLiteral s) =>
        Stringa(s, _conoscenza.RuoloPrincipale(s.Value), ListeBase.PrincipaleDiSistema(s.Value));

    private void Stringa(Literal? letterale, Ruolo ruolo, bool sistema)
    {
        if (letterale is not StringLiteral { FirstTokenIndex: >= 0 } s || s.Value.Length == 0) return;
        _trovati.TryAdd(s.FirstTokenIndex,
            new NomeTrovato(s.FirstTokenIndex, ruolo, s.Value, QuoteType.NotQuoted, sistema, DaStringa: true));
    }

    private void NascondiSegreto(Literal? letterale, TipoSegreto tipo)
    {
        if (letterale is { FirstTokenIndex: >= 0 })
            _segreti.TryAdd(letterale.FirstTokenIndex, new Segreto(letterale.FirstTokenIndex, tipo));
    }

    private void PrevediDropPrincipale(DropUnownedObjectStatement drop)
    {
        var ruolo = drop switch
        {
            DropLoginStatement => Ruolo.Login,
            DropUserStatement => Ruolo.Utente,
            DropRoleStatement or DropServerRoleStatement => Ruolo.RuoloDb,
            _ => (Ruolo?)null,
        };
        if (ruolo is not null) Principale(drop.Name, ruolo.Value);
    }

    private void OpzioneConIdentificatore(IdentifierPrincipalOption opzione)
    {
        if (opzione.Identifier is not { } id) return;
        switch (opzione.OptionKind)
        {
            case PrincipalOptionKind.DefaultDatabase: AggiungiDatabase(id); break;
            case PrincipalOptionKind.DefaultSchema: Aggiungi(id, Ruolo.Schema, ListeBase.SchemaPredefinito(id.Value)); break;
            case PrincipalOptionKind.Login: Principale(id, Ruolo.Login); break;
            case PrincipalOptionKind.Name: Principale(id, _ruoloNomeOpzione); break;
        }
    }

    /// <summary>GRANT / DENY / REVOKE: a chi (utenti o ruoli), su che cosa (oggetto, schema, database...) e su quali colonne.</summary>
    private void Permessi(SecurityStatement istruzione)
    {
        foreach (var principale in istruzione.Principals) PrincipaleAmbiguo(principale.Identifier);
        PrincipaleAmbiguo(istruzione.AsClause);
        foreach (var permesso in istruzione.Permissions) Colonne(permesso.Columns);

        // EXECUTE si dà solo su procedure e funzioni: se l'oggetto non è mai stato visto è una procedura, non una tabella.
        var eseguibile = istruzione.Permissions.Any(p =>
            p.Identifiers.Count > 0 && p.Identifiers[0].Value.Equals("EXECUTE", StringComparison.OrdinalIgnoreCase));
        Bersaglio(istruzione.SecurityTargetObject, eseguibile ? Ruolo.Procedura : Ruolo.Tabella);
    }

    private void Bersaglio(SecurityTargetObject? bersaglio, Ruolo ruoloSeIgnoto = Ruolo.Tabella)
    {
        if (bersaglio is null) return;

        var nomi = bersaglio.ObjectName?.MultiPartIdentifier?.Identifiers;
        if (nomi is { Count: > 0 })
        {
            var ultimo = nomi[^1];
            switch (bersaglio.ObjectKind)
            {
                case SecurityObjectKind.Object or SecurityObjectKind.NotSpecified:
                    AggiungiPrefissi(nomi,
                        [_conoscenza.RuoloNoto(ultimo.Value) ?? ruoloSeIgnoto, Ruolo.Schema, Ruolo.Database, Ruolo.LinkedServer],
                        nomi.Count >= 2 ? nomi[^2].Value : null);
                    break;
                case SecurityObjectKind.Schema: Aggiungi(ultimo, Ruolo.Schema, ListeBase.SchemaPredefinito(ultimo.Value)); break;
                case SecurityObjectKind.Database: AggiungiDatabase(ultimo); break;
                case SecurityObjectKind.Login: Principale(ultimo, Ruolo.Login); break;
                case SecurityObjectKind.User: Principale(ultimo, Ruolo.Utente); break;
                case SecurityObjectKind.Role or SecurityObjectKind.ServerRole or SecurityObjectKind.ApplicationRole:
                    Principale(ultimo, Ruolo.RuoloDb);
                    break;
            }
        }
        Colonne(bersaglio.Columns);
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
