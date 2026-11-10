using Microsoft.JSInterop;

namespace Anonimizzatore.Interfaccia.Servizi;

public enum SceltaTema
{
    /// <summary>Come il PC (chiaro o scuro).</summary>
    Sistema,
    Chiaro,
    Scuro,
    AltoContrasto,
}

/// <summary>Il tema dell'interfaccia: chiaro, scuro, alto contrasto o quello del sistema. Si ricorda tra un avvio e l'altro.</summary>
public sealed class Tema(ModuloJs js)
{
    public SceltaTema Scelta { get; private set; } = SceltaTema.Sistema;

    public event Action? Cambiato;

    public static string Etichetta(SceltaTema tema) => tema switch
    {
        SceltaTema.Chiaro => "Chiaro",
        SceltaTema.Scuro => "Scuro",
        SceltaTema.AltoContrasto => "Alto contrasto",
        _ => "Come il sistema",
    };

    private static string Codice(SceltaTema tema) => tema switch
    {
        SceltaTema.Chiaro => "light",
        SceltaTema.Scuro => "dark",
        SceltaTema.AltoContrasto => "contrast",
        _ => "sistema",
    };

    /// <summary>Legge il tema salvato (il tema è già applicato alla pagina; qui si allinea solo lo stato).</summary>
    public async Task InizializzaAsync()
    {
        var modulo = await js.Interop();
        Scelta = (await modulo.InvokeAsync<string>("temaSalvato")) switch
        {
            "light" => SceltaTema.Chiaro,
            "dark" => SceltaTema.Scuro,
            "contrast" => SceltaTema.AltoContrasto,
            _ => SceltaTema.Sistema,
        };
        Cambiato?.Invoke();
    }

    public async Task ImpostaAsync(SceltaTema tema)
    {
        var modulo = await js.Interop();
        await modulo.InvokeVoidAsync("impostaTema", Codice(tema));
        Scelta = tema;
        Cambiato?.Invoke();
    }
}
