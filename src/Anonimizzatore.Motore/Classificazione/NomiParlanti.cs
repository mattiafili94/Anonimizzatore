using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>Che cosa contiene una variabile (o una colonna) secondo il suo nome.</summary>
public enum Contenuto
{
    Nessuno,
    Password,
    Database,
    Server,
    Schema,
    Login,
    Job,
}

/// <summary>
/// Il nome di una variabile dice che cosa contiene: <c>@DBName</c>, <c>@DatabaseName</c>, <c>@SrcDb</c> → un database;
/// <c>@Server</c>, <c>@HostName</c> → un server; <c>@Pwd</c> → una password; <c>@SchemaName</c>, <c>@UserName</c>,
/// <c>@JobName</c>... Serve per il valore scritto come stringa (<c>DECLARE @DBName nvarchar(128) = 'Vendite'</c>), che altrimenti non si
/// riconoscerebbe come nome di un database. Stesso criterio di <c>:setvar</c> di SQLCMD. Il nome si divide in parole
/// (<c>DBName</c> → db, name; <c>NomeDatabase</c> → nome, database).
/// </summary>
public static class NomiParlanti
{
    private static readonly HashSet<string> Password = ["pass", "password", "passwd", "pwd", "psw", "secret"];
    private static readonly HashSet<string> Database = ["db", "database", "dbname", "catalog", "catalogo"];
    private static readonly HashSet<string> Server = ["server", "srv", "instance", "istanza", "host", "hostname", "node", "nodo"];
    private static readonly HashSet<string> Schema = ["schema"];
    private static readonly HashSet<string> Login = ["user", "username", "login", "utente", "account", "owner"];
    private static readonly HashSet<string> Job = ["job"];

    // Se il nome parla di un percorso, di un file o di un testo, non è un nome di oggetto (ci pensano le regole dei percorsi).
    private static readonly HashSet<string> Escludi =
        ["path", "dir", "folder", "file", "cartella", "percorso", "url", "mail", "email", "note", "nota", "desc", "descr", "description", "msg", "message", "text", "testo", "list", "elenco"];

    /// <summary>Il contenuto di una variabile o di una colonna, dal nome (con o senza <c>@</c>).</summary>
    public static Contenuto Da(string nome)
    {
        var parole = Parole.Dividi(nome.TrimStart('@'));
        if (parole.Count == 0 || parole.Any(Escludi.Contains)) return Contenuto.Nessuno;
        bool Ha(HashSet<string> insieme) => parole.Any(insieme.Contains);

        if (Ha(Password)) return Contenuto.Password;
        if (Ha(Job)) return Contenuto.Job;
        if (Ha(Schema)) return Contenuto.Schema;
        if (Ha(Login)) return Contenuto.Login;
        if (Ha(Server)) return Contenuto.Server;
        if (Ha(Database)) return Contenuto.Database;
        return Contenuto.Nessuno;
    }

    /// <summary>Per le colonne messe a confronto con una stringa (<c>database_name = 'Vendite'</c>) bastano database e server.</summary>
    public static Contenuto DaColonna(string nome) => Da(nome) is var c && c is Contenuto.Database or Contenuto.Server ? c : Contenuto.Nessuno;

    public static Ruolo? Ruolo(Contenuto contenuto) => contenuto switch
    {
        Contenuto.Database => Mappa.Ruolo.Database,
        Contenuto.Server => Mappa.Ruolo.Server,
        Contenuto.Schema => Mappa.Ruolo.Schema,
        Contenuto.Login => Mappa.Ruolo.Login,
        Contenuto.Job => Mappa.Ruolo.Job,
        _ => null,
    };
}
