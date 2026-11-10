using System.Text.RegularExpressions;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Spezza un nome in parole su underscore, CamelCase e cifre:
/// <c>FK_Clienti_Ordini</c> → FK, Clienti, Ordini; <c>IdOrdine</c> → Id, Ordine; <c>XMLData2</c> → XML, Data, 2.
/// </summary>
public static partial class Parole
{
    [GeneratedRegex(@"\p{Lu}+(?!\p{Ll})|\p{Lu}?\p{Ll}+|\p{N}+|[^\p{L}\p{N}]+")]
    private static partial Regex Pezzo();

    /// <summary>Le parole del nome (in minuscolo), senza separatori.</summary>
    public static IReadOnlyList<string> Dividi(string nome) =>
        [.. Pezzo().Matches(nome).Select(m => m.Value).Where(p => p.Any(char.IsLetterOrDigit)).Select(p => p.ToLowerInvariant())];
}

/// <summary>
/// Blacklist di un profilo: nomi o parole che vanno sempre anonimizzati. Una voce coincide con un nome se le sue
/// parole compaiono di seguito tra le parole del nome: la voce <c>Acme</c> coincide con <c>AcmeOrdini</c>,
/// <c>Cod_Acme</c> e <c>@CodiceAcme</c>; la voce <c>Acme Srl</c> (o <c>AcmeSrl</c>) con <c>AcmeSrl_Dati</c>.
/// </summary>
public sealed class Blacklist
{
    private readonly List<IReadOnlyList<string>> _voci;

    public Blacklist(IEnumerable<string> voci) =>
        _voci = [.. voci.Select(v => Parole.Dividi(v)).Where(p => p.Count > 0)];

    public bool Vuota => _voci.Count == 0;

    public bool Contiene(string nome)
    {
        if (Vuota) return false;
        var parole = Parole.Dividi(nome);
        var tutteInsieme = string.Concat(parole); // "ACMESRL" è una parola sola, ma coincide con la voce "Acme Srl"
        return _voci.Any(voce => ContieneDiSeguito(parole, voce) || tutteInsieme == string.Concat(voce));
    }

    private static bool ContieneDiSeguito(IReadOnlyList<string> parole, IReadOnlyList<string> voce)
    {
        for (var i = 0; i + voce.Count <= parole.Count; i++)
        {
            var uguali = true;
            for (var j = 0; j < voce.Count && uguali; j++) uguali = parole[i + j] == voce[j];
            if (uguali) return true;
        }
        return false;
    }
}
