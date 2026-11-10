using Anonimizzatore.Motore.Profili;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Anonimizzatore.Interfaccia.Servizi;

/// <summary>Ciò che cambia tra l'app e l'anteprima nel browser.</summary>
/// <param name="CartellaDati">Dove stanno i profili e le impostazioni (<c>%APPDATA%\Anonimizzatore</c> nell'app).</param>
/// <param name="Versione">Il numero di versione che si vede nell'app.</param>
/// <param name="Anteprima">Vero nell'anteprima di sviluppo (cifratura finta): lo dice anche l'interfaccia.</param>
public sealed record OpzioniApp(string CartellaDati, string Versione, bool Anteprima = false);

public static class ServiziInterfaccia
{
    /// <summary>Registra i servizi dell'interfaccia. L'host (app o anteprima) dice dove stanno i dati e come si cifrano.</summary>
    public static IServiceCollection AddInterfaccia(this IServiceCollection servizi, OpzioniApp opzioni, IProtezioneDati protezione)
    {
        servizi.AddSingleton(opzioni);
        servizi.AddSingleton(protezione);
        servizi.AddScoped<ModuloJs>();
        servizi.AddScoped<Tema>();
        servizi.AddScoped<Appunti>();
        servizi.AddScoped<Notifiche>();
        servizi.AddScoped<StatoApp>();
        return servizi;
    }
}

/// <summary>Il modulo JavaScript delle funzioni del browser (appunti, tema), caricato alla prima richiesta.</summary>
public sealed class ModuloJs(IJSRuntime js)
{
    private IJSObjectReference? _modulo;

    public async ValueTask<IJSObjectReference> Interop() =>
        _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Anonimizzatore.Interfaccia/js/interop.js");
}
