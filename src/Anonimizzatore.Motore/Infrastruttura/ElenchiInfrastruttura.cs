using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Infrastruttura;

/// <summary>Elenchi fissi: nomi di sistema che restano in chiaro e estensioni/TLD riconosciuti.</summary>
internal static partial class ElenchiInfrastruttura
{
    /// <summary>Cartelle di sistema comuni (nome intero di un segmento del percorso).</summary>
    public static readonly HashSet<string> CartelleSistema = new(NomeSql.Confronto)
    {
        "windows", "system32", "syswow64", "winsxs", "program files", "program files (x86)", "programdata",
        "microsoft sql server", "microsoft", "sql server", "mssql", "binn", "data", "backup", "backups", "log",
        "logs", "jobs", "repldata", "ftdata", "temp", "tmp", "users", "documents and settings", "documents",
        "desktop", "downloads", "appdata", "local", "roaming", "public", "default", "default user", "all users",
        "common files", "sql", "scripts", "inetpub", "wwwroot", "pipe", "query",
        // radici tipiche dei percorsi Linux
        "var", "opt", "mnt", "home", "usr", "etc", "root", "srv", "media", "lib", "bin", "sbin", "proc",
    };

    /// <summary>Nomi di sistema con spazi: servono in modalità testo libero, dove lo spazio chiude il percorso.</summary>
    public static readonly string[] CartelleConSpazi =
    [
        "Program Files (x86)", "Documents and Settings", "Microsoft SQL Server", "Common Files", "Program Files",
        "All Users", "Default User",
    ];

    /// <summary>Cartelle che contengono nomi di profili utente: il segmento successivo è un nome di persona.</summary>
    public static readonly HashSet<string> CartelleProfili = new(NomeSql.Confronto)
    {
        "users", "home", "documents and settings",
    };

    public static readonly HashSet<string> ProfiliGenerici = new(NomeSql.Confronto)
    {
        "public", "default", "default user", "all users", "administrator", "administrators",
    };

    /// <summary>Parole generiche: un nome fatto solo di queste (e di numeri) resta in chiaro.</summary>
    public static readonly HashSet<string> ParoleGeneriche = new(NomeSql.Confronto)
    {
        "backup", "backups", "bak", "full", "diff", "differential", "log", "logs", "data", "dati", "db", "temp",
        "tmp", "test", "prod", "dev", "old", "new", "copy", "copia", "mdf", "ndf", "ldf", "trn", "sql", "script",
        "scripts", "export", "import", "dump", "restore", "ssis", "job", "jobs", "system", "master", "model",
        "msdb", "tempdb", "mssqlsystemresource", "mastlog", "modellog", "msdbdata", "msdblog", "templog",
        "errorlog", "agent", "files", "file", "program", "microsoft", "server", "mssql", "mssqlserver", "windows", "users",
        "user", "public", "default", "pipe", "query",
        // prefissi dei nomi già anonimizzati: così analizzare due volte lo stesso testo non cambia nulla
        "srv", "dom", "dir", "ip", "mail", "sch", "tbl", "vw", "usp", "fn", "trg", "usr", "rol",
    };

    public static readonly HashSet<string> DatabaseDiSistema = new(NomeSql.Confronto)
    {
        "master", "model", "msdb", "tempdb", "distribution", "mssqlsystemresource",
    };

    /// <summary>Estensioni riconosciute come fine di un percorso (serve a chiudere un percorso con spazi).</summary>
    public static readonly HashSet<string> EstensioniNote = new(NomeSql.Confronto)
    {
        "bak", "mdf", "ndf", "ldf", "trn", "sql", "csv", "tsv", "txt", "log", "xml", "json", "dtsx", "xls", "xlsx",
        "zip", "7z", "rar", "gz", "dll", "exe", "ps1", "cmd", "bat", "mdb", "accdb", "dat", "cer", "pfx", "bacpac",
        "dacpac", "bcp", "fmt", "trc", "xel", "sqlaudit", "bkp", "back", "rpt", "cfg", "ini", "config", "pdf",
        "doc", "docx", "sqlplan", "dmp", "tmp", "mdx", "ssmssln", "sln", "key", "pem",
    };

    /// <summary>
    /// Suffissi (TLD) accettati per un host "da solo" nel testo. Elenco volutamente corto: <c>no</c>, <c>at</c>,
    /// <c>es</c>… sono anche nomi di colonna (<c>t.no</c>) e non devono essere scambiati per domini.
    /// </summary>
    public static readonly HashSet<string> TldTestoLibero = new(NomeSql.Confronto)
    {
        "com", "net", "org", "edu", "gov", "info", "biz", "eu", "it", "local", "lan", "corp", "internal",
        "intranet", "localdomain", "priv", "invalid", "test",
    };

    /// <summary>Suffissi accettati quando il contesto (URL, UNC, email) conferma già che è un host.</summary>
    public static readonly HashSet<string> TldContesto = new(NomeSql.Confronto)
    {
        "com", "net", "org", "edu", "gov", "info", "biz", "eu", "it", "local", "lan", "corp", "internal",
        "intranet", "localdomain", "priv", "invalid", "test", "de", "fr", "es", "uk", "ch", "at", "nl", "be", "pt",
        "pl", "se", "no", "dk", "fi", "ie", "us", "ca", "io", "co", "cloud", "app", "dev", "home", "domain",
    };

    /// <summary>Etichette tipiche di T-SQL che non sono mai parte di un host.</summary>
    public static readonly HashSet<string> EtichetteSql = new(NomeSql.Confronto)
    {
        "dbo", "sys", "information_schema", "inserted", "deleted", "guest",
    };

    [GeneratedRegex(@"^(?:[A-Za-z]\$|admin\$|ipc\$|print\$)$", RegexOptions.IgnoreCase)]
    public static partial Regex CondivisioneAmministrativa();

    [GeneratedRegex(@"^(?<pre>MSSQL\d*)(?:\.(?<istanza>.+))?$", RegexOptions.IgnoreCase)]
    public static partial Regex CartellaIstanzaSql();
}
