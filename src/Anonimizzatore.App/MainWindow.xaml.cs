using System.IO;
using System.Reflection;
using System.Windows;
using Anonimizzatore.Interfaccia.Servizi;
using Anonimizzatore.Motore.Profili;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;

namespace Anonimizzatore.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // I dati dell'utente stanno fuori dalla cartella dell'app: aggiornare = sostituire la cartella, i profili restano.
        var cartellaDati = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anonimizzatore");
        var versione = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? "0.0.0";
        versione = versione.Split('+')[0]; // senza l'identificativo di build

        var servizi = new ServiceCollection();
        servizi.AddWpfBlazorWebView();
        servizi.AddInterfaccia(new OpzioniApp(cartellaDati, versione), new ProtezioneDpapi());
        Vista.Services = servizi.BuildServiceProvider();

        // La cache del motore web sta con i dati locali dell'utente, non accanto all'eseguibile.
        Vista.BlazorWebViewInitializing += (_, e) =>
            e.UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anonimizzatore", "WebView2");

        // L'app non apre altre finestre né siti: un link esterno non fa nulla.
        Vista.BlazorWebViewInitialized += (_, e) =>
            e.WebView.CoreWebView2.NewWindowRequested += (_, a) => a.Handled = true;
    }
}
