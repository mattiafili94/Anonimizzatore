using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Motore.Anonimizzazione;

/// <summary>
/// La blacklist e i nomi già anonimizzati dentro le stringhe (messaggi, testi, <c>NAME = N'Vendite full'</c> di un backup,
/// la parte finale di un URL...). Non si tagliano le stringhe: si cambiano solo le parole che altrove sarebbero state
/// anonimizzate. In quest'ordine:
/// <list type="number">
/// <item><c>DOMINIO\utente</c> con il dominio in blacklist o già noto: <c>dom_01\usr_001</c>, come nei login;</item>
/// <item>le voci della blacklist di più parole (<c>Acme Srl</c>): un solo <c>txt_NNN</c> per la frase;</item>
/// <item>i nomi già in mappa (tabelle, database, server...), parole intere: lo stesso nome anonimo che hanno negli script;</item>
/// <item>le altre parole: se coincidono con un nome in mappa prendono quello (anche dentro <c>Vendite_FULL</c>), se contengono
/// una parola della blacklist diventano <c>txt_NNN</c> (nome nuovo e stabile, ripristinabile dalla mappa).</item>
/// </list>
/// Il resto del testo (dati, parole comuni, numeri) non si tocca. I nomi già anonimi (<c>db_01</c>, <c>txt_001</c>) non si ritoccano.
/// </summary>
internal sealed partial class TerminiNelleStringhe
{
    private readonly MappaCliente _mappa;
    private readonly Blacklist _blacklist;
    private readonly Regex? _frasi;

    public TerminiNelleStringhe(Profilo profilo, MappaCliente mappa)
    {
        _mappa = mappa;
        Attivo = profilo.NomiNelleStringhe;
        _blacklist = new Blacklist(profilo.Blacklist);

        var frasi = profilo.Blacklist.Select(v => Parole.Dividi(v)).Where(p => p.Count >= 2)
            .Select(p => string.Join(@"[\s_.\-]*", p.Select(Regex.Escape))).ToList();
        if (frasi.Count > 0)
            _frasi = new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", frasi) + @")(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Spento dal profilo: le stringhe restano come sono (le segnala l'anti-fuga).</summary>
    public bool Attivo { get; }

    public string Sostituisci(string testo, SqlDinamico.Dizionario dizionario)
    {
        if (!Attivo || testo.Length == 0) return testo;

        // 1. DOMINIO\utente
        testo = DominioUtente().Replace(testo, m =>
        {
            var dominio = m.Groups["d"].Value;
            var utente = m.Groups["u"].Value;
            if (EAnonimo(dominio) || EAnonimo(utente)) return m.Value;
            if (!_blacklist.Contiene(dominio) && !_mappa.Contiene(Ruolo.Dominio, dominio)) return m.Value;
            return _mappa.Ottieni(Ruolo.Dominio, dominio) + "\\" + _mappa.Ottieni(Ruolo.Utente, utente);
        });

        // 2. voci della blacklist di più parole
        if (_frasi is not null)
            testo = _frasi.Replace(testo, m => _mappa.Ottieni(Ruolo.Termine, m.Value));

        // 3. nomi già in mappa, parole intere (anche tra parentesi quadre, o con spazi nel nome)
        testo = dizionario.Sostituisci(testo);

        // 4. parola per parola: un nome in mappa dentro un nome composto, oppure una parola della blacklist
        var sb = new StringBuilder(testo.Length);
        var ultimo = 0;
        foreach (Match m in ParolaOAnonimo().Matches(testo))
        {
            if (!m.Groups["parola"].Success) continue;
            var parola = m.Value;
            if (!parola.Any(char.IsLetter)) continue; // numeri: dati, non nomi

            string? nuovo = null;
            if (parola.Length >= 2) nuovo = dizionario.Cerca(parola);
            if (nuovo is null && _blacklist.Contiene(parola)) nuovo = _mappa.Ottieni(Ruolo.Termine, parola);
            if (nuovo is null) continue;

            sb.Append(testo, ultimo, m.Index - ultimo).Append(nuovo);
            ultimo = m.Index + m.Length;
        }

        return ultimo == 0 ? testo : sb.Append(testo, ultimo, testo.Length - ultimo).ToString();
    }

    private static bool EAnonimo(string nome) => NomeAnonimoIntero().IsMatch(nome);

    // ZETA\mario.rossi (non \\server\condivisione, non una cartella dopo un'altra: niente barra o lettera prima del dominio)
    [GeneratedRegex(@"(?<![\p{L}\p{N}_\\\-])(?<d>[\p{L}\p{N}_\-]+)\\(?<u>[\p{L}\p{N}_.\-]+)")]
    private static partial Regex DominioUtente();

    // Un nome già anonimo (da non toccare) oppure una parola (lettere e cifre: l'underscore separa).
    [GeneratedRegex(
        @"(?<anonimo>(?<![\p{L}\p{N}@#])(?:(?:srv|db|sch|dom|ag|txt|tbl|vw|usp|fn|trg|job|usr|rol|ix|pk|fk|df|ck|uq|al|dir|file|ip)_\d+" +
        @"|mail_\d+@example\.invalid|col_[a-z][a-z0-9]*(?:_\d+)?_\d+)(?![\p{L}\p{N}]))|(?<parola>(?<![\p{L}\p{N}@#$])[\p{L}\p{N}]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParolaOAnonimo();

    [GeneratedRegex(
        @"^(?:(?:srv|db|sch|dom|ag|txt|usr|rol|tbl|vw|usp|fn|trg|job|ix|pk|fk|df|ck|uq|al|dir|file|ip)_\d+|mail_\d+@example\.invalid)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NomeAnonimoIntero();
}
