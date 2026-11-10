using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Classificazione;

/// <summary>
/// Whitelist di un profilo: nomi che restano in chiaro anche se il tipo di oggetto andrebbe anonimizzato.
/// <list type="bullet">
/// <item><b>Per nome intero</b>: una voce coincide con un nome uguale (senza maiuscole e parentesi).</item>
/// <item><b>Per parole</b>: il nome si spezza su underscore, CamelCase e cifre; se <b>tutte</b> le parole sono
/// generiche (lista base in <see cref="ListeBase.ParoleGeneriche"/> più le voci a una sola parola del cliente)
/// resta in chiaro. Le cifre contano come generiche.</item>
/// </list>
/// Una voce di più parole (<c>OrdiniAperti</c>, <c>Servizio Backup</c>) vale solo per nome intero: non rende
/// generiche le sue parole, altrimenti <c>Ordini</c> da solo uscirebbe in chiaro.
/// <b>La blacklist vince sempre</b>: chi usa questa classe controlla prima la <see cref="Blacklist"/>.
/// </summary>
public sealed class Whitelist
{
    private readonly HashSet<string> _nomiInteri = new(NomeSql.Confronto);
    private readonly HashSet<string> _paroleGeneriche = new(ListeBase.ParoleGeneriche, StringComparer.OrdinalIgnoreCase);

    public Whitelist(IEnumerable<string> vociDelCliente)
    {
        foreach (var voce in vociDelCliente)
        {
            var nome = NomeSql.Normalizza(voce);
            var parole = Parole.Dividi(nome);
            if (parole.Count == 0) continue;

            _nomiInteri.Add(nome);
            if (parole.Count == 1) _paroleGeneriche.Add(parole[0]);
        }
    }

    public bool Contiene(string nome)
    {
        if (_nomiInteri.Contains(NomeSql.Normalizza(nome))) return true;

        var parole = Parole.Dividi(nome);
        return parole.Count > 0 && parole.All(p => _paroleGeneriche.Contains(p) || p.All(char.IsDigit));
    }
}
