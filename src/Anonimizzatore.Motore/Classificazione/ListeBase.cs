using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Liste fisse di SQL Server: oggetti di sistema che restano sempre in chiaro (l'IA li conosce già).
/// Nessun nome di cliente: qui ci sono solo nomi che esistono in ogni installazione.
/// </summary>
public static class ListeBase
{
    private static readonly HashSet<string> SchemiPredefiniti = new(NomeSql.Confronto)
    {
        "dbo", "sys", "INFORMATION_SCHEMA", "guest",
    };

    // Negli schemi "sys" e "INFORMATION_SCHEMA" ogni oggetto è di sistema.
    private static readonly HashSet<string> SchemiSoloSistema = new(NomeSql.Confronto)
    {
        "sys", "INFORMATION_SCHEMA",
    };

    private static readonly HashSet<string> DatabaseSistema = new(NomeSql.Confronto)
    {
        "master", "model", "msdb", "tempdb", "distribution", "mssqlsystemresource",
    };

    // Viste di compatibilità senza schema (sysobjects, syscolumns...): ancora molto usate negli script vecchi.
    private static readonly HashSet<string> VisteLegacy = new(NomeSql.Confronto)
    {
        "sysobjects", "syscolumns", "sysindexes", "sysindexkeys", "sysusers", "sysdatabases", "sysprocesses",
        "syscomments", "sysfiles", "sysfilegroups", "sysforeignkeys", "sysconstraints", "sysdepends", "syslogins",
        "sysmessages", "sysservers", "systypes", "sysreferences", "sysperfinfo", "syspermissions", "sysprotects",
        "syscurconfigs", "sysconfigures", "syslanguages", "syslockinfo", "sysmembers", "sysxlogins", "sysdevices",
    };

    private static readonly string[] PrefissiProcedureSistema =
    [
        "sp_help", "sp_cursor", "sp_trace_", "sp_xml_", "sp_cdc_", "sp_fulltext_", "sp_replication", "sp_repl",
        "sp_ms", "sp_syscollector", "sp_add_job", "sp_update_job", "sp_delete_job", "sp_start_job", "sp_stop_job",
        "sp_add_schedule", "sp_update_schedule", "sp_delete_schedule", "sp_attach_schedule", "sp_detach_schedule",
        "sp_add_operator", "sp_update_operator", "sp_delete_operator", "sp_add_alert", "sp_update_alert",
        "sp_delete_alert", "sp_add_notification", "sp_add_category", "sp_add_proxy", "sp_grant_login_to_proxy",
        "sp_addlinkedserver", "sp_droplinkedserver", "sp_addlinkedsrvlogin", "sp_droplinkedsrvlogin",
    ];

    private static readonly HashSet<string> ProcedureSistema = new(NomeSql.Confronto)
    {
        "sp_executesql", "sp_prepare", "sp_execute", "sp_unprepare", "sp_prepexec", "sp_prepexecrpc",
        "sp_spaceused", "sp_who", "sp_who2", "sp_lock", "sp_rename", "sp_renamedb", "sp_configure",
        "sp_addrolemember", "sp_droprolemember", "sp_addsrvrolemember", "sp_dropsrvrolemember",
        "sp_addlogin", "sp_droplogin", "sp_adduser", "sp_dropuser", "sp_grantdbaccess", "sp_revokedbaccess",
        "sp_change_users_login", "sp_changedbowner", "sp_password", "sp_defaultdb", "sp_defaultlanguage",
        "sp_serveroption", "sp_linkedservers", "sp_addserver", "sp_dropserver", "sp_attach_db", "sp_detach_db",
        "sp_refreshview", "sp_refreshsqlmodule", "sp_recompile", "sp_updatestats", "sp_createstats", "sp_autostats",
        "sp_tableoption", "sp_dboption", "sp_depends", "sp_columns", "sp_tables", "sp_stored_procedures",
        "sp_fkeys", "sp_pkeys", "sp_statistics", "sp_databases", "sp_datatype_info", "sp_sproc_columns",
        "sp_special_columns", "sp_server_info", "sp_describe_first_result_set",
        "sp_describe_undeclared_parameters", "sp_addmessage", "sp_dropmessage", "sp_altermessage",
        "sp_readerrorlog", "sp_cycle_errorlog", "sp_enumerrorlogs", "sp_send_dbmail", "sp_addextendedproperty",
        "sp_updateextendedproperty", "sp_dropextendedproperty", "sp_set_session_context", "sp_getapplock",
        "sp_releaseapplock", "sp_addtype", "sp_droptype", "sp_validatelogins", "sp_addumpdevice",
        "sp_dropdevice", "sp_addrole", "sp_droprole", "sp_testlinkedserver", "sp_dropremotelogin", "sp_addremotelogin",
        "sp_add_jobstep", "sp_update_jobstep", "sp_delete_jobstep", "sp_add_jobserver", "sp_delete_jobserver",
        "sp_add_jobschedule", "sp_delete_jobschedule", "sp_help_jobschedule",
        "sp_msforeachtable", "sp_msforeachdb", "sp_oacreate", "sp_oamethod", "sp_oadestroy", "sp_oagetproperty",
        "sp_oasetproperty", "sp_oageterrorinfo", "sp_start_job", "sp_stop_job", "sp_delete_backuphistory",
        "xp_cmdshell", "xp_msver", "xp_fixeddrives", "xp_fileexist", "xp_dirtree", "xp_subdirs", "xp_create_subdir",
        "xp_delete_file", "xp_availablemedia", "xp_enumgroups", "xp_logininfo", "xp_loginconfig", "xp_readerrorlog",
        "xp_instance_regread", "xp_instance_regwrite", "xp_regread", "xp_regwrite", "xp_regdeletevalue",
        "xp_sqlagent_enum_jobs", "xp_sendmail", "xp_servicecontrol", "xp_sprintf", "xp_sscanf",
    };

    private static readonly HashSet<string> FunzioniSistema = new(NomeSql.Confronto)
    {
        "fn_my_permissions", "fn_listextendedproperty", "fn_trace_gettable", "fn_trace_getinfo", "fn_dblog",
        "fn_dump_dblog", "fn_virtualfilestats", "fn_get_audit_file", "fn_xe_file_target_read_file",
        "fn_varbintohexstr", "fn_varbintohexsubstring", "fn_helpcollations", "fn_servershareddrives",
        "fn_validate_plan_guide", "fn_cdc_get_min_lsn", "fn_cdc_get_max_lsn", "fn_cdc_map_time_to_lsn",
        "fn_cdc_map_lsn_to_time", "fn_cdc_increment_lsn", "fn_cdc_decrement_lsn", "fn_cdc_has_column_changed",
        "fn_cdc_is_bit_set", "fn_cdc_get_column_ordinal", "fn_hadr_backup_is_preferred_replica",
        "fn_hadr_is_primary_replica",
    };

    // Metodi di tipi (XML, hierarchyid, geography...) che si scrivono come una chiamata di funzione con prefisso
    // (colonna.value(...), @geo.STDistance(...)): non sono funzioni dell'utente.
    private static readonly HashSet<string> MetodiDiOggetto = new(NomeSql.Confronto)
    {
        "value", "query", "nodes", "exist", "modify",
        "GetAncestor", "GetDescendant", "GetLevel", "GetRoot", "IsDescendantOf", "Read", "Reparent", "ToString",
        "Parse", "Lat", "Long", "Z", "M", "Point", "Reduce",
    };

    /// <summary>
    /// Whitelist base: parole generiche, tecniche o di uso comune che non rivelano nulla del cliente. Un nome le cui
    /// parole sono <b>tutte</b> generiche (<c>usp_GetData</c>, <c>LogStorico</c>) resta in chiaro: all'IA dice cosa fa.
    /// Parole di dominio (clienti, ordini, fatture...) non ci sono: stanno in chiaro solo se le aggiunge il cliente.
    /// </summary>
    public static IReadOnlySet<string> ParoleGeneriche { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // sigle tecniche e prefissi di convenzione
        "usp", "sp", "xp", "fn", "ufn", "vw", "tbl", "tb", "trg", "tr", "pk", "fk", "ix", "idx", "ux", "df", "ck", "uq", "tmp", "temp",
        // verbi e azioni
        "get", "set", "ins", "insert", "upd", "update", "del", "delete", "sel", "select", "merge", "list", "load", "save",
        "check", "calc", "calcola", "carica", "salva", "aggiorna", "elimina", "inserisci", "leggi", "crea", "create",
        "drop", "add", "remove", "export", "import", "sync", "run", "exec", "process", "genera", "verifica", "controlla",
        // preposizioni e congiunzioni
        "by", "for", "from", "to", "and", "or", "of", "in", "on", "per", "con", "da", "di", "del", "e", "il", "la", "le", "lo",
        // nomi generici (italiano e inglese)
        "id", "ids", "key", "data", "dati", "date", "time", "ora", "anno", "year", "mese", "month", "giorno", "day",
        "codice", "code", "cod", "stato", "status", "tipo", "type", "tipologia", "descrizione", "description", "desc",
        "nome", "name", "note", "notes", "numero", "number", "num", "nr", "valore", "value", "importo", "amount", "totale",
        "total", "quantita", "qty", "count", "flag", "attivo", "active", "enabled", "default", "progressivo",
        "log", "logs", "storico", "history", "archivio", "archive", "elenco", "lista", "dettaglio", "dettagli", "detail",
        "details", "testata", "header", "report", "reports", "stampa", "print", "errore", "errori", "error", "errors",
        "parametri", "parametro", "param", "params", "parameters", "config", "configurazione", "settings", "impostazioni",
        "utente", "utenti", "user", "users", "ruolo", "ruoli", "role", "roles", "permessi", "permissions",
        "test", "prova", "demo", "sample", "esempio", "backup", "restore", "job", "batch", "task", "queue", "coda",
        "master", "info", "main", "base", "new", "old", "nuovo", "vecchio", "ultimo", "last", "first", "primo",
        "min", "max", "sum", "avg", "tot", "row", "rows", "col", "cols", "table", "view", "proc", "func", "all", "tutti",
    };

    // Utenti, login e ruoli che esistono in ogni installazione: l'IA li conosce e non rivelano nulla.
    private static readonly HashSet<string> PrincipaliSistema = new(NomeSql.Confronto)
    {
        "sa", "public", "dbo", "guest", "sys", "INFORMATION_SCHEMA",
        // ruoli fissi di server
        "sysadmin", "securityadmin", "serveradmin", "setupadmin", "processadmin", "diskadmin", "dbcreator", "bulkadmin",
        // ruoli fissi di database
        "db_owner", "db_accessadmin", "db_securityadmin", "db_ddladmin", "db_backupoperator", "db_datareader",
        "db_datawriter", "db_denydatareader", "db_denydatawriter",
        // msdb, SSIS, Database Mail, SQL Agent
        "db_ssisadmin", "db_ssisltduser", "db_ssisoperator", "dc_admin", "dc_operator", "dc_proxy",
        "SQLAgentUserRole", "SQLAgentReaderRole", "SQLAgentOperatorRole", "DatabaseMailUserRole",
        "TargetServersRole", "ServerGroupAdministratorRole", "ServerGroupReaderRole", "PolicyAdministratorRole",
        "UtilityCMRReader", "UtilityIMRWriter", "UtilityIMRReader", "MS_DataCollectorInternalUser",
    };

    // Domini degli account Windows di sistema: NT AUTHORITY\SYSTEM, NT SERVICE\MSSQLSERVER, BUILTIN\Administrators...
    private static readonly HashSet<string> DominiSistema = new(NomeSql.Confronto)
    {
        "NT AUTHORITY", "NT SERVICE", "BUILTIN", "NT VIRTUAL MACHINE", "Window Manager", "Font Driver Host",
    };

    /// <summary>Il server locale, comunque sia scritto (<c>(local)</c>, <c>.</c>, <c>localhost</c>): non rivela nulla.</summary>
    public static bool ServerLocale(string server)
    {
        var s = server.Trim();
        return s == "." || s.Equals("(local)", StringComparison.OrdinalIgnoreCase)
               || s.Equals("localhost", StringComparison.OrdinalIgnoreCase) || s.Equals("(localdb)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vero se il dominio è quello degli account Windows di sistema (<c>NT AUTHORITY</c>, <c>BUILTIN</c>...).</summary>
    public static bool DominioDiSistema(string dominio) => DominiSistema.Contains(NomeSql.Normalizza(dominio));

    /// <summary>
    /// Vero per login, utenti e ruoli predefiniti (<c>sa</c>, <c>public</c>, <c>db_owner</c>, <c>##MS_...##</c>) e per gli
    /// account Windows di sistema (<c>NT AUTHORITY\SYSTEM</c>): restano sempre in chiaro.
    /// </summary>
    public static bool PrincipaleDiSistema(string nome)
    {
        nome = NomeSql.Normalizza(nome);
        if (PrincipaliSistema.Contains(nome)) return true;
        if (nome.Length > 4 && nome.StartsWith("##", StringComparison.Ordinal) && nome.EndsWith("##", StringComparison.Ordinal))
            return true; // ##MS_PolicyEventProcessingLogin## e simili
        var barra = nome.IndexOf('\\');
        return barra > 0 && DominioDiSistema(nome[..barra]);
    }

    /// <summary>Schema che restano in chiaro: <c>dbo</c>, <c>sys</c>, <c>INFORMATION_SCHEMA</c>, <c>guest</c>.</summary>
    public static bool SchemaPredefinito(string schema) => SchemiPredefiniti.Contains(NomeSql.Normalizza(schema));

    /// <summary>master, msdb, model: gli oggetti che ci stanno dentro sono tutti del sistema (tempdb no: ci stanno anche oggetti dell'utente).</summary>
    public static bool DatabaseCentraleDiSistema(string database) =>
        NomeSql.Normalizza(database).ToLowerInvariant() is "master" or "msdb" or "model";

    public static bool DatabaseDiSistema(string database) => DatabaseSistema.Contains(NomeSql.Normalizza(database));

    /// <summary>Vero se il nome è un oggetto di sistema (e quindi non va mai anonimizzato).</summary>
    /// <param name="schema">Schema scritto davanti al nome, se c'è.</param>
    public static bool OggettoDiSistema(Ruolo ruolo, string nome, string? schema)
    {
        if (schema is not null && SchemiSoloSistema.Contains(NomeSql.Normalizza(schema))) return true;

        nome = NomeSql.Normalizza(nome);
        return ruolo switch
        {
            Ruolo.Procedura => ProcedureSistema.Contains(nome)
                               || PrefissiProcedureSistema.Any(p => nome.StartsWith(p, StringComparison.OrdinalIgnoreCase)),
            Ruolo.Funzione => FunzioniSistema.Contains(nome),
            Ruolo.Tabella or Ruolo.Vista => VisteLegacy.Contains(nome),
            _ => false,
        };
    }

    /// <summary>Metodo di un tipo (es. <c>value</c> di XML): da non scambiare per una funzione utente.</summary>
    public static bool MetodoDiOggetto(string nome) =>
        MetodiDiOggetto.Contains(nome) || nome.StartsWith("ST", StringComparison.Ordinal);
}
