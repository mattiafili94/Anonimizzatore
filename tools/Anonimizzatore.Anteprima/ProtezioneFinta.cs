using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Anteprima;

/// <summary>Non cifra niente: l'anteprima è solo per sviluppo e lavora su dati inventati.</summary>
internal sealed class ProtezioneFinta : IProtezioneDati
{
    public byte[] Proteggi(byte[] dati) => dati;

    public byte[] Sblocca(byte[] datiProtetti) => datiProtetti;
}
