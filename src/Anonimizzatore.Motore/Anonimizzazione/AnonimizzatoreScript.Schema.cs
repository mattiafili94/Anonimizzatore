using Anonimizzatore.Motore.Classificazione;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;
using Anonimizzatore.Motore.SchemaDb;

namespace Anonimizzatore.Motore.Anonimizzazione;

// Lo schema del database (facoltativo) serve a classificare meglio: tipi delle colonne, tabelle e viste, nomi veri.
public static partial class AnonimizzatoreScript
{
    /// <summary>
    /// Fa conoscere al motore lo schema: tabelle e viste (un <c>FROM vOrdini</c> è una vista anche senza il suo
    /// <c>CREATE</c>), i tipi delle colonne (<c>col_vc50_001</c> invece di <c>col_x_001</c>) e i nomi veri, che i nomi
    /// generati non devono mai copiare. Un <c>CREATE</c> nello script ha comunque la precedenza.
    /// </summary>
    private static void SeminaSchema(SchemaDatabase schema, Conoscenza conoscenza, MappaCliente mappa)
    {
        var nomi = new List<string>();
        foreach (var tabella in schema.Tabelle)
        {
            conoscenza.Dichiara(tabella.Nome, tabella.EVista ? Ruolo.Vista : Ruolo.Tabella);
            nomi.Add(tabella.Nome);
            if (tabella.Schema is not null) nomi.Add(tabella.Schema);
            foreach (var colonna in tabella.Colonne)
            {
                conoscenza.DichiaraTipo(Ruolo.Colonna, colonna.Nome, colonna.Tipo);
                nomi.Add(colonna.Nome);
            }
        }
        mappa.AggiungiNomiRiservati(nomi);
    }
}

/// <summary>
/// Decide e dà il nome anonimo a un nome che non viene da uno script (per esempio dallo schema), con le stesse regole
/// degli script: sistema, blacklist, matrice, whitelist. Così lo schema allegato usa gli stessi nomi degli script.
/// </summary>
internal sealed class DecisoreNomi(Profilo profilo, MappaCliente mappa)
{
    private readonly AnonimizzatoreScript.Liste _liste = new(profilo, mappa);

    /// <summary>Vero se questo nome verrebbe anonimizzato (senza creare nulla nella mappa).</summary>
    public bool DaAnonimizzare(Ruolo ruolo, string nome) =>
        nome.Length > 0
        && _liste.DaAnonimizzare(new NomeTrovato(0, ruolo, nome, Microsoft.SqlServer.TransactSql.ScriptDom.QuoteType.NotQuoted,
            ruolo == Ruolo.Schema && ListeBase.SchemaPredefinito(nome)));

    /// <summary>Il nome anonimo di <paramref name="nome"/> (o il nome stesso se resta in chiaro).</summary>
    public string Nome(Ruolo ruolo, string nome, string? tipoSql = null)
    {
        if (nome.Length == 0) return nome;
        var sistema = ruolo == Ruolo.Schema && ListeBase.SchemaPredefinito(nome);
        return _liste.DaAnonimizzare(new NomeTrovato(0, ruolo, nome, Microsoft.SqlServer.TransactSql.ScriptDom.QuoteType.NotQuoted, sistema))
            ? mappa.Ottieni(ruolo, nome, tipoSql)
            : nome;
    }

    /// <summary>Il nome di un indice o constraint: le parti che sono oggetti anonimizzati, oppure il nome intero.</summary>
    public string Composto(Ruolo ruolo, string nome)
    {
        var tutto = _liste.DaAnonimizzare(new NomeTrovato(0, ruolo, nome, Microsoft.SqlServer.TransactSql.ScriptDom.QuoteType.NotQuoted, false));
        var nuovo = tutto ? null : AnonimizzatoreScript.SostituisciParti(nome, AnonimizzatoreScript.PartiSostituibili(mappa));
        return nuovo ?? mappa.Ottieni(ruolo, nome);
    }
}
