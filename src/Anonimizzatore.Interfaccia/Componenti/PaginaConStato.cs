using Anonimizzatore.Interfaccia.Servizi;
using Microsoft.AspNetCore.Components;

namespace Anonimizzatore.Interfaccia.Componenti;

/// <summary>Base delle pagine: si ridisegnano quando lo stato dell'app cambia.</summary>
public abstract class PaginaConStato : ComponentBase, IDisposable
{
    [Inject] protected StatoApp Stato { get; set; } = null!;
    [Inject] protected Notifiche Notifiche { get; set; } = null!;
    [Inject] protected Appunti Appunti { get; set; } = null!;
    [Inject] protected NavigationManager Navigazione { get; set; } = null!;

    protected override void OnInitialized() => Stato.Cambiato += Aggiorna;

    private void Aggiorna() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        Stato.Cambiato -= Aggiorna;
        GC.SuppressFinalize(this);
    }
}
