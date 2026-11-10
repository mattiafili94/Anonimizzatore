using System.Globalization;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.SchemaDb;

/// <summary>Esito di <see cref="ImportSchema.Leggi"/>: lo schema, oppure il motivo per cui non si legge; più gli avvisi.</summary>
public sealed record RisultatoImportSchema(SchemaDatabase? Schema, IReadOnlyList<string> Avvisi, string? Errore)
{
    public bool Riuscito => Schema is not null;
}

/// <summary>
/// Import dello schema di un database. L'app non si collega al server: dà una <see cref="Query"/> di sola lettura da eseguire in
/// SSMS nel database del cliente; l'utente copia la griglia dei risultati (con le intestazioni) e la incolla nell'app.
/// </summary>
/// <remarks>
/// La query dà un solo risultato, a righe, senza NULL (le celle vuote sono stringhe vuote). Colonne:
/// <c>Tipo</c> (T tabella, V vista, C colonna, PK chiave primaria, IX indice, FK chiave esterna), <c>Schema</c>, <c>Oggetto</c>,
/// <c>Nome</c> (colonna, vincolo o indice), <c>Posizione</c>, <c>Dettaglio</c> (tipo della colonna, o colonna del vincolo),
/// <c>Extra1</c>..<c>Extra3</c> (C: NULLABLE/NOTNULL e IDENTITY; IX: UNIQUE; FK: schema, tabella e colonna riferite).
/// </remarks>
public static class ImportSchema
{
    public const string Query = """
        -- Anonimizzatore T-SQL: lettura dello schema (solo SELECT, non modifica niente).
        -- 1) Eseguila nel database del cliente.  2) Copia TUTTA la griglia dei risultati CON le intestazioni
        --    (in SSMS: clic sulla griglia, poi Ctrl+Maiusc+C).  3) Incollala nell'app.
        SET NOCOUNT ON;

        SELECT N'T' AS Tipo, s.name AS [Schema], o.name AS Oggetto, N'' AS Nome, 0 AS Posizione, N'' AS Dettaglio, N'' AS Extra1, N'' AS Extra2, N'' AS Extra3
        FROM sys.tables AS o JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0
        UNION ALL
        SELECT N'V', s.name, o.name, N'', 0, N'', N'', N'', N''
        FROM sys.views AS o JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0
        UNION ALL
        SELECT N'C', s.name, o.name, c.name, c.column_id,
               CASE WHEN t.name IN (N'varchar', N'char', N'varbinary', N'binary', N'nvarchar', N'nchar')
                         THEN t.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max'
                                                   WHEN t.name IN (N'nvarchar', N'nchar') THEN CAST(c.max_length / 2 AS nvarchar(10))
                                                   ELSE CAST(c.max_length AS nvarchar(10)) END + N')'
                    WHEN t.name IN (N'decimal', N'numeric') THEN t.name + N'(' + CAST(c.precision AS nvarchar(10)) + N',' + CAST(c.scale AS nvarchar(10)) + N')'
                    WHEN t.name IN (N'datetime2', N'datetimeoffset', N'time') THEN t.name + N'(' + CAST(c.scale AS nvarchar(10)) + N')'
                    ELSE t.name END,
               CASE WHEN c.is_nullable = 1 THEN N'NULLABLE' ELSE N'NOTNULL' END,
               CASE WHEN c.is_identity = 1 THEN N'IDENTITY' ELSE N'' END,
               N''
        FROM sys.columns AS c
        JOIN sys.objects AS o ON o.object_id = c.object_id AND o.type IN (N'U', N'V') AND o.is_ms_shipped = 0
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        JOIN sys.types AS t ON t.system_type_id = c.system_type_id AND t.user_type_id = t.system_type_id
        UNION ALL
        SELECT CASE WHEN i.is_primary_key = 1 THEN N'PK' ELSE N'IX' END, s.name, o.name, i.name, ic.key_ordinal, col.name,
               CASE WHEN i.is_unique = 1 THEN N'UNIQUE' ELSE N'' END, N'', N''
        FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 AND ic.key_ordinal > 0
        JOIN sys.columns AS col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        JOIN sys.objects AS o ON o.object_id = i.object_id AND o.type IN (N'U', N'V') AND o.is_ms_shipped = 0
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE i.type > 0 AND i.is_hypothetical = 0 AND i.name IS NOT NULL
        UNION ALL
        SELECT N'FK', s.name, o.name, fk.name, fkc.constraint_column_id, c.name, rs.name, ro.name, rc.name
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.objects AS o ON o.object_id = fk.parent_object_id
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        JOIN sys.columns AS c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        JOIN sys.objects AS ro ON ro.object_id = fkc.referenced_object_id
        JOIN sys.schemas AS rs ON rs.schema_id = ro.schema_id
        JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id;
        """;

    private static readonly HashSet<string> CodiciNoti = ["T", "V", "C", "PK", "IX", "FK"];

    private sealed class TabellaInCostruzione(string? schema, string nome, bool vista)
    {
        public string? Schema { get; } = schema;
        public string Nome { get; } = nome;
        public bool EVista { get; set; } = vista;
        public List<(int Pos, ColonnaSchema Colonna)> Colonne { get; } = [];
        public string? NomePk { get; set; }
        public List<(int Pos, string Col)> Pk { get; } = [];
        public Dictionary<string, (bool Unico, List<(int Pos, string Col)> Colonne)> Indici { get; } = new(NomeSql.Confronto);
        public Dictionary<string, (string? SchemaRif, string TabellaRif, List<(int Pos, string Col, string ColRif)> Colonne)> ChiaviEsterne { get; } = new(NomeSql.Confronto);
    }

    /// <summary>Legge il risultato della <see cref="Query"/> incollato dall'utente (testo con tabulazioni, oppure CSV).</summary>
    public static RisultatoImportSchema Leggi(string? testo)
    {
        var righe = (testo ?? "").Split('\n').Select(r => r.TrimEnd('\r')).Where(r => r.Trim().Length > 0).ToList();
        if (righe.Count == 0) return Errore("Non c'è niente da leggere: incolla il risultato della query.");

        var prima = righe[0];
        var separatore = prima.Contains('\t') ? '\t' : prima.Count(c => c == ';') > prima.Count(c => c == ',') ? ';' : ',';

        var tabelle = new Dictionary<(string, string), TabellaInCostruzione>(new ChiaveTabella());
        var avvisi = new List<string>();
        var righeIgnorate = 0;
        var primaRiga = true;

        foreach (var riga in righe)
        {
            var celle = Dividi(riga, separatore);
            var tipo = celle[0].Trim().ToUpperInvariant();
            if (primaRiga)
            {
                primaRiga = false;
                if (tipo == "TIPO") continue; // intestazione
            }
            if (!CodiciNoti.Contains(tipo) || celle.Count < 6)
            {
                righeIgnorate++;
                continue;
            }

            string Cella(int i) => i < celle.Count && !celle[i].Equals("NULL", StringComparison.OrdinalIgnoreCase) ? celle[i].Trim() : "";
            var schema = Cella(1);
            var oggetto = Cella(2);
            if (oggetto.Length == 0)
            {
                righeIgnorate++;
                continue;
            }

            if (!tabelle.TryGetValue((schema, oggetto), out var t))
                tabelle[(schema, oggetto)] = t = new TabellaInCostruzione(schema.Length == 0 ? null : schema, oggetto, tipo == "V");
            if (tipo == "V") t.EVista = true;

            var nome = Cella(3);
            _ = int.TryParse(Cella(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos);
            var dettaglio = Cella(5);

            switch (tipo)
            {
                case "C":
                    if (nome.Length == 0 || dettaglio.Length == 0) { righeIgnorate++; break; }
                    t.Colonne.Add((pos, new ColonnaSchema(nome, dettaglio, !Cella(6).Equals("NOTNULL", StringComparison.OrdinalIgnoreCase),
                        Cella(7).Equals("IDENTITY", StringComparison.OrdinalIgnoreCase))));
                    break;
                case "PK":
                    t.NomePk = nome.Length == 0 ? null : nome;
                    t.Pk.Add((pos, dettaglio));
                    break;
                case "IX":
                    if (!t.Indici.TryGetValue(nome, out var ix)) t.Indici[nome] = ix = (Cella(6).Equals("UNIQUE", StringComparison.OrdinalIgnoreCase), []);
                    ix.Colonne.Add((pos, dettaglio));
                    break;
                case "FK":
                    if (!t.ChiaviEsterne.TryGetValue(nome, out var fk))
                        t.ChiaviEsterne[nome] = fk = (Cella(6).Length == 0 ? null : Cella(6), Cella(7), []);
                    fk.Colonne.Add((pos, dettaglio, Cella(8)));
                    break;
            }
        }

        if (tabelle.Count == 0)
            return Errore("Non riconosco il formato. Esegui la query dell'app e copia la griglia dei risultati con le intestazioni (Ctrl+Maiusc+C in SSMS).");

        var risultato = new List<TabellaSchema>();
        foreach (var t in tabelle.Values.OrderBy(x => x.Schema, NomeSql.Confronto).ThenBy(x => x.Nome, NomeSql.Confronto))
        {
            if (t.Colonne.Count == 0)
            {
                avvisi.Add($"«{t.Nome}» non ha colonne nel risultato: l'ho saltata.");
                continue;
            }
            risultato.Add(new TabellaSchema(
                t.Schema, t.Nome,
                [.. t.Colonne.OrderBy(c => c.Pos).Select(c => c.Colonna)],
                t.Pk.Count == 0 ? null : new ChiavePrimariaSchema(t.NomePk ?? "", [.. t.Pk.OrderBy(p => p.Pos).Select(p => p.Col)]),
                [.. t.ChiaviEsterne.Select(f => new ChiaveEsternaSchema(
                    f.Key, [.. f.Value.Colonne.OrderBy(c => c.Pos).Select(c => c.Col)],
                    f.Value.SchemaRif, f.Value.TabellaRif, [.. f.Value.Colonne.OrderBy(c => c.Pos).Select(c => c.ColRif)]))],
                [.. t.Indici.Select(i => new IndiceSchema(i.Key, i.Value.Unico, [.. i.Value.Colonne.OrderBy(c => c.Pos).Select(c => c.Col)]))],
                t.EVista));
        }

        if (risultato.Count == 0) return Errore("Il risultato non contiene colonne: controlla di aver copiato tutta la griglia.");
        if (righeIgnorate > 0) avvisi.Add($"{righeIgnorate} righe non si sono capite e le ho ignorate.");
        return new RisultatoImportSchema(new SchemaDatabase(risultato), avvisi, null);
    }

    private static RisultatoImportSchema Errore(string messaggio) => new(null, [], messaggio);

    // Divide una riga in celle; accetta le celle tra virgolette ("a;b", con "" per una virgoletta), come fanno SSMS e il CSV.
    private static List<string> Dividi(string riga, char separatore)
    {
        var celle = new List<string>();
        var corrente = new System.Text.StringBuilder();
        var tra = false;
        for (var i = 0; i < riga.Length; i++)
        {
            var c = riga[i];
            if (tra)
            {
                if (c == '"' && i + 1 < riga.Length && riga[i + 1] == '"') { corrente.Append('"'); i++; }
                else if (c == '"') tra = false;
                else corrente.Append(c);
            }
            else if (c == '"' && corrente.Length == 0) tra = true;
            else if (c == separatore) { celle.Add(corrente.ToString()); corrente.Clear(); }
            else corrente.Append(c);
        }
        celle.Add(corrente.ToString());
        return celle;
    }

    private sealed class ChiaveTabella : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            NomeSql.Confronto.Equals(x.Item1, y.Item1) && NomeSql.Confronto.Equals(x.Item2, y.Item2);

        public int GetHashCode((string, string) o) => HashCode.Combine(NomeSql.Confronto.GetHashCode(o.Item1), NomeSql.Confronto.GetHashCode(o.Item2));
    }
}
