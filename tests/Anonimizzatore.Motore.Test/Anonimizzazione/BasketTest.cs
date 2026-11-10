using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Controllo;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>
/// "Basket" di script SQL Server tipici (query, procedure, backup/restore, sicurezza, chiavi, DML, gruppi di disponibilità,
/// job, linked server). Tutti con nomi INVENTATI. Il cliente di prova si chiama "Zeta" (nella blacklist).
/// </summary>
public class BasketTest
{
    private static string Esempio(string nome) => Aiuti.LeggiEsempio(Path.Combine("basket", nome + ".sql"));

    public static TheoryData<string> Script => new(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Esempi", "basket"), "*.sql")
            .Select(f => Path.GetFileNameWithoutExtension(f)).Order().ToList());

    [Theory]
    [MemberData(nameof(Script))]
    public void Ogni_script_si_anonimizza_e_torna_uguale(string nome)
    {
        var originale = Esempio(nome);
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo("Zeta");

        var risultato = AnonimizzatoreScript.Anonimizza([originale], profilo, mappa);

        var s = risultato.Script[0];
        Assert.True(s.Anonimizzato, string.Join("; ", s.Avvisi.Select(a => a.Messaggio)));
        // parole chiave mai scambiate per nomi
        foreach (var vietato in new[] { "DISK", "WINDOWS", "URL", "TAPE" })
            Assert.DoesNotContain(mappa.Voci, v => v.Originale.Equals(vietato, StringComparison.OrdinalIgnoreCase));

        // andata e ritorno: uguale all'originale, tranne password (mai ripristinate) e commenti (qui senza regole di righe vuote)
        var ripristinato = Aiuti.RipristinaPerProva(s.Testo!, mappa);
        string Pulisci(string t) => string.Join("\n", t.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));
        var righeOriginali = Pulisci(originale).Split('\n');
        var righeRipristinate = Pulisci(ripristinato).Split('\n');
        Assert.Equal(righeOriginali.Length, righeRipristinate.Length);
        for (var i = 0; i < righeOriginali.Length; i++)
        {
            if (righeRipristinate[i].Contains("<password>")) continue; // le password non tornano
            Assert.Equal(righeOriginali[i], righeRipristinate[i], ignoreCase: true);
        }
    }

    [Fact]
    public void Gli_script_dei_gruppi_di_disponibilita_non_lasciano_nomi_in_chiaro()
    {
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo("Zeta");
        var testi = new[] { Esempio("08-ag-aggiungi-db"), Esempio("09-ag-rimuovi-db") };

        var risultato = AnonimizzatoreScript.Anonimizza(testi, profilo, mappa);
        var esito = ControlloAntiFuga.Verifica(testi, risultato, mappa, profilo);

        Assert.DoesNotContain(esito.Fughe, f => f.Gravita == GravitaFuga.Bloccante);
        var tutto = string.Join("\n", risultato.Script.Select(s => s.Testo));
        foreach (var nome in new[] { "AGZeta", "VenditeZeta", "SRVSQL01", "SRVSQL02", "SRVSQL03", "LSNZeta", "zeta.local" })
            Assert.DoesNotContain(nome, tutto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ag_01", tutto);
    }

    [Fact]
    public void Le_tabelle_di_msdb_restano_in_chiaro_anche_senza_prefisso_del_database()
    {
        var testo = Aiuti.Anon("SELECT j.name FROM dbo.sysjobs j JOIN dbo.sysjobhistory h ON h.job_id = j.job_id;");

        Assert.Contains("dbo.sysjobs", testo);
        Assert.Contains("dbo.sysjobhistory", testo);
    }

    [Fact]
    public void Dopo_FROM_le_parole_DISK_URL_e_WINDOWS_non_sono_tabelle_nemmeno_in_modalita_di_riserva()
    {
        // BACKUP ... TO URL con CREDENTIAL non è letto dal parser: si va in modalità di riserva
        var mappa = new MappaCliente();
        var testo = Aiuti.Anon(
            "BACKUP DATABASE [Vendite] TO URL = N'https://conto.blob.core.windows.net/b/v.bak' WITH CREDENTIAL = N'cred';\n" +
            "RESTORE VERIFYONLY FROM DISK = N'D:\b\v.bak';", mappa);

        Assert.Contains("FROM DISK =", testo);
        Assert.DoesNotContain(mappa.Voci, v => v.Originale.Equals("DISK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Il_gruppo_di_disponibilita_ha_il_suo_nome_ag()
    {
        var mappa = new MappaCliente();
        var testo = Aiuti.Anon(
            "ALTER AVAILABILITY GROUP [AgVendite] ADD DATABASE [Vendite];\n" +
            "ALTER DATABASE [Vendite] SET HADR AVAILABILITY GROUP = [AgVendite];\n" +
            "DROP AVAILABILITY GROUP [AgVendite];", mappa);

        Assert.Equal(
            "ALTER AVAILABILITY GROUP [ag_01] ADD DATABASE [db_01];\n" +
            "ALTER DATABASE [db_01] SET HADR AVAILABILITY GROUP = [ag_01];\n" +
            "DROP AVAILABILITY GROUP [ag_01];", testo.Replace("\r", ""));
    }
}
