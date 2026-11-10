namespace Anonimizzatore.Interfaccia.Servizi;

public sealed record Notifica(Guid Id, string Testo);

/// <summary>
/// Conferme brevi che non chiedono nulla ("Testo copiato"): una alla volta in basso, spariscono da sole. Gli errori non passano da
/// qui: restano a schermo finché non si risolvono.
/// </summary>
public sealed class Notifiche
{
    private readonly List<Notifica> _elenco = [];

    public IReadOnlyList<Notifica> Elenco => _elenco;

    public event Action? Cambiato;

    public void Mostra(string testo, TimeSpan? durata = null)
    {
        var notifica = new Notifica(Guid.NewGuid(), testo);
        _elenco.Add(notifica);
        Cambiato?.Invoke();
        _ = Task.Delay(durata ?? TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            _elenco.Remove(notifica);
            Cambiato?.Invoke();
        });
    }
}
