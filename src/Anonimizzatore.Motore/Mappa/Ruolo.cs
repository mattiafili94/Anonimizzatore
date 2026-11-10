namespace Anonimizzatore.Motore.Mappa;

/// <summary>Che cosa rappresenta un nome nello script. Decide il prefisso del nome anonimo.</summary>
public enum Ruolo
{
    Server,
    LinkedServer,
    Database,
    Schema,
    Tabella,
    Vista,
    Procedura,
    Funzione,
    Trigger,
    Job,
    Login,
    Utente,
    RuoloDb,
    Dominio,
    Indice,
    PrimaryKey,
    ForeignKey,
    Default,
    Check,
    Unique,
    Colonna,
    Variabile,
    Parametro,
    Temporanea,

    /// <summary>Alias di tabella e nomi di CTE (gli alias di colonna sono <see cref="Colonna"/>: si usano come colonne).</summary>
    Alias,

    // Livello 3 (infrastruttura): percorsi, IP, email
    Cartella,
    File,
    Ip,
    Email,
}

/// <summary>
/// "Famiglia" di un ruolo: ruoli della stessa famiglia condividono contatore e nomi.
/// Esempio: un login e un utente con lo stesso nome diventano lo stesso <c>usr_007</c>.
/// </summary>
public static class FamigliaRuolo
{
    public static string Di(Ruolo ruolo) => ruolo switch
    {
        Ruolo.Server or Ruolo.LinkedServer => "srv",
        Ruolo.Database => "db",
        Ruolo.Schema => "sch",
        Ruolo.Dominio => "dom",
        Ruolo.Tabella => "tbl",
        Ruolo.Vista => "vw",
        Ruolo.Procedura => "usp",
        Ruolo.Funzione => "fn",
        Ruolo.Trigger => "trg",
        Ruolo.Job => "job",
        Ruolo.Login or Ruolo.Utente => "usr",
        Ruolo.RuoloDb => "rol",
        Ruolo.Indice => "ix",
        Ruolo.PrimaryKey => "pk",
        Ruolo.ForeignKey => "fk",
        Ruolo.Default => "df",
        Ruolo.Check => "ck",
        Ruolo.Unique => "uq",
        Ruolo.Colonna => "col",
        Ruolo.Variabile or Ruolo.Parametro => "var",
        Ruolo.Temporanea => "tmp",
        Ruolo.Alias => "al",
        Ruolo.Cartella => "dir",
        Ruolo.File => "file",
        Ruolo.Ip => "ip",
        Ruolo.Email => "mail",
        _ => throw new ArgumentOutOfRangeException(nameof(ruolo), ruolo, null),
    };
}
