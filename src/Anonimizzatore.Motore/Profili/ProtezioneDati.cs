using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Anonimizzatore.Motore.Profili;

/// <summary>Cifra e decifra i dati salvati su disco. Nei test si usa una versione finta.</summary>
public interface IProtezioneDati
{
    byte[] Proteggi(byte[] dati);
    byte[] Sblocca(byte[] datiProtetti);
}

/// <summary>
/// Cifratura Windows DPAPI legata all'utente: solo lo stesso utente sullo stesso PC può leggere i file.
/// Con un PC nuovo i profili non si recuperano (vedi "backup esportabile", idea futura).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtezioneDpapi : IProtezioneDati
{
    // Valore fisso dell'app: un altro programma dello stesso utente non decifra i file "per caso".
    private static readonly byte[] Entropia = "Anonimizzatore T-SQL / profili v1"u8.ToArray();

    public byte[] Proteggi(byte[] dati) =>
        ProtectedData.Protect(dati, Entropia, DataProtectionScope.CurrentUser);

    public byte[] Sblocca(byte[] datiProtetti) =>
        ProtectedData.Unprotect(datiProtetti, Entropia, DataProtectionScope.CurrentUser);
}
