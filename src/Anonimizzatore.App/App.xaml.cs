using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Anonimizzatore.App;

public partial class App : Application
{
    private void AlAvvio(object sender, StartupEventArgs e)
    {
        // L'interfaccia gira dentro WebView2 (il motore di Edge, già presente in Windows 10/11 aggiornati).
        // Se manca, si dice chiaramente cosa fare invece di mostrare una finestra vuota.
        if (!WebView2Disponibile())
        {
            MessageBox.Show(
                "Per funzionare questa app ha bisogno di Microsoft Edge WebView2 Runtime, che su questo PC non risulta installato.\n\n" +
                "Scaricalo dal sito di Microsoft (cerca «WebView2 Runtime», versione Evergreen Standalone), installalo e riavvia l'app.",
                "Anonimizzatore T-SQL", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }

    private static bool WebView2Disponibile()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or DllNotFoundException)
        {
            return false;
        }
    }
}
