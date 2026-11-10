using Anonimizzatore.Anteprima;
using Anonimizzatore.Interfaccia.Layout;
using Anonimizzatore.Interfaccia.Servizi;

// SOLO SVILUPPO: la stessa interfaccia dell'app, in un browser. I profili stanno in una cartella temporanea e non sono cifrati.
var builder = WebApplication.CreateBuilder(args);

// Un risultato di query dello schema incollato può superare il limite predefinito di 32 KB.
builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(o => o.MaximumReceiveMessageSize = 16 * 1024 * 1024);
var cartellaDati = Path.Combine(Path.GetTempPath(), "anonimizzatore-anteprima");
builder.Services.AddInterfaccia(new OpzioniApp(cartellaDati, "anteprima", Anteprima: true), new ProtezioneFinta());

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<HostAnteprima>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(App).Assembly);

app.Run();
