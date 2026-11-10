using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Motore.SchemaDb;

/// <summary>
/// Lo "schema anonimizzato allegato": il DDL delle tabelle citate negli script, con colonne e tipi, chiave primaria, chiavi
/// esterne e indici, scritto con gli <b>stessi nomi anonimi</b> degli script (stesse regole di sistema, blacklist, matrice e
/// whitelist, stessa mappa). All'IA dice di che tipo sono le colonne e come sono collegate le tabelle, anche quelle che gli
/// script non definiscono. Una tabella è "citata" se il suo nome anonimo (o il nome vero, se resta in chiaro) compare negli
/// script; si aggiungono le tabelle a cui le citate puntano con una chiave esterna.
/// </summary>
public static partial class SchemaAnonimizzato
{
    private static readonly HashSet<string> ParoleDaQuotare = new(StringComparer.OrdinalIgnoreCase)
    {
        "table", "order", "group", "user", "key", "index", "select", "from", "where", "view", "default", "check", "column",
        "constraint", "primary", "foreign", "references", "schema", "database", "type", "file", "identity", "values", "public",
    };

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_@#$]*$")]
    private static partial Regex IdentificatoreRegolare();

    /// <summary>Il DDL anonimizzato delle tabelle citate nei testi; stringa vuota se non ne cita nessuna.</summary>
    /// <param name="testiAnonimizzati">Gli script già anonimizzati (servono a capire quali tabelle sono citate).</param>
    public static string Genera(
        SchemaDatabase schema, IEnumerable<string> testiAnonimizzati, Profilo profilo, MappaCliente mappa)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(testiAnonimizzati);
        ArgumentNullException.ThrowIfNull(profilo);
        ArgumentNullException.ThrowIfNull(mappa);

        var testo = string.Join("\n", testiAnonimizzati);
        var decisore = new DecisoreNomi(profilo, mappa);

        // Le tabelle citate; poi quelle a cui puntano le chiavi esterne delle citate (un livello).
        var incluse = schema.Tabelle.Where(t => Citata(t, testo, decisore, mappa)).ToList();
        foreach (var riferita in incluse.SelectMany(t => t.ChiaviEsterne ?? [])
                     .Select(fk => Trova(schema, fk.SchemaRiferito, fk.TabellaRiferita)).OfType<TabellaSchema>().ToList())
            if (!incluse.Contains(riferita)) incluse.Add(riferita);
        if (incluse.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var tabella in schema.Tabelle.Where(incluse.Contains))
        {
            if (sb.Length > 0) sb.AppendLine();
            Scrivi(sb, tabella, schema, decisore);
        }
        return sb.ToString().TrimEnd();
    }

    // Una tabella è citata se compare negli script con il nome che ha lì: l'anonimo, oppure il vero se resta in chiaro.
    private static bool Citata(TabellaSchema t, string testo, DecisoreNomi decisore, MappaCliente mappa)
    {
        var ruolo = t.EVista ? Ruolo.Vista : Ruolo.Tabella;
        var voce = mappa.Voci.FirstOrDefault(v => v.Ruolo is Ruolo.Tabella or Ruolo.Vista && NomeSql.Confronto.Equals(v.Originale, t.Nome));
        if (voce is not null) return ContieneParola(testo, voce.Anonimo);
        return !decisore.DaAnonimizzare(ruolo, t.Nome) && ContieneParola(testo, t.Nome);
    }

    private static bool ContieneParola(string testo, string parola) =>
        Regex.IsMatch(testo, @"(?<![\w@#$])" + Regex.Escape(parola) + @"(?![\w$])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static TabellaSchema? Trova(SchemaDatabase schema, string? nomeSchema, string nome) =>
        schema.Tabelle.FirstOrDefault(t => NomeSql.Confronto.Equals(t.Nome, nome)
                                           && (nomeSchema is null || t.Schema is null || NomeSql.Confronto.Equals(t.Schema, nomeSchema)));

    private static void Scrivi(StringBuilder sb, TabellaSchema t, SchemaDatabase schema, DecisoreNomi decisore)
    {
        var nome = Qualificato(t, decisore);

        if (t.EVista)
        {
            var colonneVista = t.Colonne.Select(c => $"{Quota(decisore.Nome(Ruolo.Colonna, c.Nome, c.Tipo))} {c.Tipo}");
            sb.AppendLine($"-- VISTA {nome} ({string.Join(", ", colonneVista)})");
            return;
        }

        sb.AppendLine($"CREATE TABLE {nome}");
        sb.AppendLine("(");
        var righe = new List<string>();
        foreach (var c in t.Colonne)
            righe.Add($"    {Quota(decisore.Nome(Ruolo.Colonna, c.Nome, c.Tipo))} {c.Tipo}{(c.EIdentity ? " IDENTITY" : "")}{(c.AmmetteNull ? " NULL" : " NOT NULL")}");
        if (t.ChiavePrimaria is { } pk)
            righe.Add($"    CONSTRAINT {Quota(decisore.Composto(Ruolo.PrimaryKey, pk.Nome))} PRIMARY KEY ({Colonne(pk.Colonne, t, decisore)})");
        sb.AppendLine(string.Join(",\n", righe));
        sb.AppendLine(");");

        foreach (var fk in t.ChiaviEsterne ?? [])
        {
            var riferita = Trova(schema, fk.SchemaRiferito, fk.TabellaRiferita);
            var nomeRiferita = riferita is not null
                ? Qualificato(riferita, decisore)
                : Quota(decisore.Nome(Ruolo.Tabella, fk.TabellaRiferita));
            sb.AppendLine($"ALTER TABLE {nome} ADD CONSTRAINT {Quota(decisore.Composto(Ruolo.ForeignKey, fk.Nome))} " +
                          $"FOREIGN KEY ({Colonne(fk.Colonne, t, decisore)}) REFERENCES {nomeRiferita} ({Colonne(fk.ColonneRiferite, riferita, decisore)});");
        }

        foreach (var ix in t.Indici ?? [])
            sb.AppendLine($"CREATE {(ix.Unico ? "UNIQUE " : "")}INDEX {Quota(decisore.Composto(Ruolo.Indice, ix.Nome))} ON {nome} ({Colonne(ix.Colonne, t, decisore)});");
    }

    private static string Qualificato(TabellaSchema t, DecisoreNomi decisore)
    {
        var tabella = Quota(decisore.Nome(t.EVista ? Ruolo.Vista : Ruolo.Tabella, t.Nome));
        return t.Schema is null ? tabella : Quota(decisore.Nome(Ruolo.Schema, t.Schema)) + "." + tabella;
    }

    private static string Colonne(IEnumerable<string> nomi, TabellaSchema? tabella, DecisoreNomi decisore) =>
        string.Join(", ", nomi.Select(n => Quota(decisore.Nome(Ruolo.Colonna, n,
            tabella?.Colonne.FirstOrDefault(c => NomeSql.Confronto.Equals(c.Nome, n))?.Tipo))));

    /// <summary>Tra parentesi quadre solo se serve: nome non regolare o parola riservata.</summary>
    private static string Quota(string nome) =>
        IdentificatoreRegolare().IsMatch(nome) && !ParoleDaQuotare.Contains(nome) ? nome : "[" + nome.Replace("]", "]]") + "]";
}
