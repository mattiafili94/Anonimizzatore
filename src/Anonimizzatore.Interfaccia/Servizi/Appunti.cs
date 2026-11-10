using Microsoft.JSInterop;

namespace Anonimizzatore.Interfaccia.Servizi;

/// <summary>Gli appunti del PC. Nessun dato esce dal PC: si copia soltanto negli appunti locali.</summary>
public sealed class Appunti(ModuloJs js)
{
    /// <summary>Copia il testo. Restituisce <c>false</c> se il sistema non l'ha permesso.</summary>
    public async Task<bool> CopiaAsync(string testo)
    {
        var modulo = await js.Interop();
        return await modulo.InvokeAsync<bool>("copiaNegliAppunti", testo);
    }

    /// <summary>Il testo negli appunti, o <c>null</c> se non si può leggere (il sistema può chiedere il permesso).</summary>
    public async Task<string?> IncollaAsync()
    {
        var modulo = await js.Interop();
        return await modulo.InvokeAsync<string?>("leggiDagliAppunti");
    }
}
