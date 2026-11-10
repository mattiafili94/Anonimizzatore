namespace Anonimizzatore.Motore.SchemaDb;

// Lo schema di un database, "in memoria": tabelle, viste, colonne con i tipi, chiave primaria, chiavi esterne e indici.
// Nella fase 2 lo riempirà la query di import (sys.objects, sys.columns, ...); qui il motore lo accetta già, per:
//  - conoscere i tipi delle colonne (col_vc50_001 invece di col_x_001) e distinguere tabelle e viste;
//  - produrre lo "schema anonimizzato allegato": il DDL delle tabelle citate, con gli stessi nomi anonimi degli script.

/// <param name="Tipo">Il tipo come lo scrive T-SQL: <c>varchar(50)</c>, <c>decimal(18,2)</c>, <c>int</c>.</param>
public sealed record ColonnaSchema(string Nome, string Tipo, bool AmmetteNull = true, bool EIdentity = false);

public sealed record ChiavePrimariaSchema(string Nome, IReadOnlyList<string> Colonne);

public sealed record ChiaveEsternaSchema(
    string Nome,
    IReadOnlyList<string> Colonne,
    string? SchemaRiferito,
    string TabellaRiferita,
    IReadOnlyList<string> ColonneRiferite);

public sealed record IndiceSchema(string Nome, bool Unico, IReadOnlyList<string> Colonne);

/// <summary>Una tabella o una vista dello schema.</summary>
public sealed record TabellaSchema(
    string? Schema,
    string Nome,
    IReadOnlyList<ColonnaSchema> Colonne,
    ChiavePrimariaSchema? ChiavePrimaria = null,
    IReadOnlyList<ChiaveEsternaSchema>? ChiaviEsterne = null,
    IReadOnlyList<IndiceSchema>? Indici = null,
    bool EVista = false);

public sealed record SchemaDatabase(IReadOnlyList<TabellaSchema> Tabelle);
