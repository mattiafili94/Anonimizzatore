using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Legenda;

/// <summary>
/// La legenda in testa al testo che va all'IA: spiega la convenzione dei nomi (<c>tbl_</c> = tabella, <c>col_vc50_</c> =
/// colonna varchar(50), <c>/* c001 */</c> = commento rimosso...) e chiede all'IA di usare quei nomi tali e quali, così la
/// risposta si può de-anonimizzare. Spiega <b>solo ciò che compare davvero</b> nel testo: una legenda lunga e piena di
/// convenzioni inutilizzate distrae.
/// </summary>
public static partial class GeneratoreLegenda
{
    private static readonly (string Modello, string Esempio, string Significato)[] Nomi =
    [
        (@"\bdb_\d{2,}\b", "db_01", "database"),
        (@"\bsch_\d{2,}\b", "sch_01", "schema"),
        (@"\bsrv_\d{2,}\b", "srv_01", "server, istanza o server collegato"),
        (@"\bdom_\d{2,}\b", "dom_01", "dominio Windows / Active Directory"),
        (@"\btbl_\d{3,}\b", "tbl_001", "tabella"),
        (@"\bvw_\d{3,}\b", "vw_001", "vista"),
        (@"\busp_\d{3,}\b", "usp_001", "stored procedure"),
        (@"\bfn_\d{3,}\b", "fn_001", "funzione"),
        (@"\btrg_\d{3,}\b", "trg_001", "trigger"),
        (@"\bjob_\d{3,}\b", "job_001", "job di SQL Agent"),
        (@"\bag_\d{2,}\b", "ag_01", "gruppo di disponibilità (Always On)"),
        (@"\btxt_\d{3,}\b", "txt_001", "parola o nome sostituito dentro un testo (stringa o messaggio)"),
        (@"\busr_\d{3,}\b", "usr_001", "login o utente"),
        (@"\brol_\d{3,}\b", "rol_001", "ruolo"),
        (@"\bix_\d{3,}\b", "ix_001", "indice"),
        (@"\b(?:pk|fk|df|ck|uq)_\d{3,}\b", "pk_001, fk_001, df_001, ck_001, uq_001", "constraint (chiave primaria, esterna, default, check, unique)"),
        (@"\bal_\d{3,}\b", "al_001", "alias di tabella"),
        (@"(?<![\w#])##?tmp_\d{3,}\b", "#tmp_001", "tabella temporanea"),
        (@"\bdir_\d{3,}\b", "dir_001", "cartella di un percorso"),
        (@"\bfile_\d{3,}\b", "file_001", "file (o nome logico di file)"),
        (@"\bip_\d{3,}\b", "ip_001", "indirizzo IP"),
        (@"\bmail_\d{3,}@example\.invalid\b", "mail_001@example.invalid", "indirizzo email"),
    ];

    [GeneratedRegex(@"\bcol_(?<t>[a-z][a-z0-9]*(?:_\d+)?)_\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex ColonnaConTipo();

    [GeneratedRegex(@"@[vp]_(?<t>[a-z][a-z0-9]*(?:_\d+)?)_\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VariabileConTipo();

    [GeneratedRegex(@"@p_[a-z][a-z0-9]*(?:_\d+)?_\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Parametro();

    [GeneratedRegex(@"/\* c\d{3,} \*/")]
    private static partial Regex Commento();

    [GeneratedRegex(@"0x0{32}\b")]
    private static partial Regex SidSegnaposto();

    [GeneratedRegex(@"\b(?:IX|PK|FK|DF|CK|UQ)_\w*(?:tbl|vw|usp|fn|trg|sch|db|srv)_\d+", RegexOptions.IgnoreCase)]
    private static partial Regex NomeComposto();

    /// <summary>La legenda per il testo dato (script anonimizzati e schema); ogni riga inizia con <c>--</c>.</summary>
    public static string Genera(string testoAnonimizzato)
    {
        ArgumentNullException.ThrowIfNull(testoAnonimizzato);

        var sb = new StringBuilder();
        sb.AppendLine("-- LEGENDA (generata dall'app, non fa parte degli script)");
        sb.AppendLine("-- Gli script sono stati anonimizzati: i nomi veri sono sostituiti da nomi convenzionali. Nella tua risposta");
        sb.AppendLine("-- usa questi nomi esattamente come sono (stessa ortografia, anche nel codice nuovo che scrivi) e non");
        sb.AppendLine("-- provare a indovinare quelli veri. I nomi senza un prefisso convenzionale (per esempio colonne e alias) sono");
        sb.AppendLine("-- quelli veri.");

        foreach (var (modello, esempio, significato) in Nomi)
            if (Regex.IsMatch(testoAnonimizzato, modello, RegexOptions.IgnoreCase))
                sb.AppendLine($"--   {esempio}: {significato}");

        if (NomeComposto().IsMatch(testoAnonimizzato))
            sb.AppendLine("--   Un nome di indice o constraint che contiene un nome convenzionale (FK_tbl_001_tbl_002, IX_tbl_001_Data) è formato da quelle parti.");

        var tipiColonne = CodiciTipo(ColonnaConTipo(), testoAnonimizzato);
        if (tipiColonne.Count > 0)
            sb.AppendLine("--   col_<tipo>_NNN: colonna con il nome nascosto; <tipo> è il suo tipo di dato" + ElencoTipi(tipiColonne));

        var tipiVariabili = CodiciTipo(VariabileConTipo(), testoAnonimizzato);
        if (tipiVariabili.Count > 0)
            sb.AppendLine((Parametro().IsMatch(testoAnonimizzato) ? "--   @v_<tipo>_NNN: variabile; @p_<tipo>_NNN: parametro" : "--   @v_<tipo>_NNN: variabile")
                          + " con il nome nascosto, <tipo> come sopra" + (tipiColonne.Count > 0 ? "" : ElencoTipi(tipiVariabili)));

        if (Commento().IsMatch(testoAnonimizzato))
            sb.AppendLine("--   /* c001 */: un commento dello script originale, rimosso. Se riscrivi il codice lascia i segnaposto dove stanno.");

        if (testoAnonimizzato.Contains("<password>", StringComparison.Ordinal))
            sb.AppendLine("--   <password>: una password, rimossa. Non chiederla e non inventarla.");

        if (SidSegnaposto().IsMatch(testoAnonimizzato))
            sb.AppendLine("--   0x00000000000000000000000000000000: un SID, rimosso.");

        return sb.ToString().TrimEnd();
    }

    private static SortedSet<string> CodiciTipo(Regex regex, string testo) =>
        new(regex.Matches(testo).Select(m => m.Groups["t"].Value.ToLowerInvariant()), StringComparer.Ordinal);

    /// <summary>": vc50 = varchar(50), int = int, x = tipo non noto".</summary>
    private static string ElencoTipi(SortedSet<string> codici)
    {
        var voci = codici.Select(c => c == CodiceTipo.Ignoto
            ? "x = tipo non noto"
            : CodiceTipo.Descrivi(c) is { } tipo ? $"{c} = {tipo}" : c);
        return " (" + string.Join(", ", voci) + ")";
    }
}
