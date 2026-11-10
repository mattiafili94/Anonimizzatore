using Anonimizzatore.Motore.Anonimizzazione;
using Anonimizzatore.Motore.Controllo;
using Anonimizzatore.Motore.Legenda;
using Anonimizzatore.Motore.Mappa;
using Anonimizzatore.Motore.Profili;

namespace Anonimizzatore.Motore.Test.Anonimizzazione;

/// <summary>La blacklist e i nomi già anonimizzati dentro le stringhe. Nomi INVENTATI ("Acme", "Vendite", "Ordini").</summary>
public class TerminiNelleStringheTest
{
    private static (string Testo, MappaCliente Mappa, Profilo Profilo) Esegui(string sql, params string[] blacklist)
    {
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo(blacklist);
        return (Aiuti.Anon(sql, mappa, profilo), mappa, profilo);
    }

    [Fact]
    public void Una_parola_della_blacklist_in_una_stringa_diventa_un_termine_e_il_resto_resta()
    {
        var (testo, mappa, _) = Esegui("SELECT * FROM dbo.Ordini WHERE Nota LIKE N'%Acme%' AND Stato = 'A';", "Acme");

        Assert.Contains("LIKE N'%txt_001%'", testo);
        Assert.Contains("Stato = 'A'", testo);
        Assert.DoesNotContain("Acme", testo);
        Assert.Equal("SELECT * FROM dbo.Ordini WHERE Nota LIKE N'%Acme%' AND Stato = 'A';", Aiuti.RipristinaPerProva(testo, mappa));
    }

    [Fact]
    public void Si_cambia_tutta_la_parola_che_contiene_il_termine_e_non_i_dati_normali()
    {
        var (testo, _, _) = Esegui("PRINT N'Backup VenditeAcme completato per Mario Rossi, 125 righe';", "Acme");

        Assert.Equal("PRINT N'Backup txt_001 completato per Mario Rossi, 125 righe';", testo);
    }

    [Fact]
    public void Un_nome_gia_anonimizzato_negli_script_ha_lo_stesso_nome_anche_nelle_stringhe()
    {
        var (testo, mappa, _) = Esegui(
            "SELECT * FROM dbo.Ordini;\nPRINT N'Ordini aperti';\nBACKUP DATABASE Vendite TO DISK = N'D:\\b\\x.bak' WITH NAME = N'Vendite_full';");

        Assert.Contains("PRINT N'tbl_001 aperti';", testo);
        Assert.Contains("NAME = N'db_01_full'", testo);
        Assert.Equal(
            "SELECT * FROM dbo.Ordini;\nPRINT N'Ordini aperti';\nBACKUP DATABASE Vendite TO DISK = N'D:\\b\\x.bak' WITH NAME = N'Vendite_full';",
            Aiuti.RipristinaPerProva(testo, mappa).Replace("\r", ""));
    }

    [Fact]
    public void Una_voce_di_piu_parole_diventa_un_solo_termine_e_torna_senza_parentesi()
    {
        var (testo, mappa, _) = Esegui("PRINT N'Cliente: Acme Srl, sede di Roma';", "Acme Srl");

        Assert.Equal("PRINT N'Cliente: txt_001, sede di Roma';", testo);
        Assert.Equal("PRINT N'Cliente: Acme Srl, sede di Roma';", Aiuti.RipristinaPerProva(testo, mappa));
    }

    [Fact]
    public void Dominio_e_utente_in_una_stringa_seguono_i_nomi_dei_login()
    {
        var (testo, mappa, _) = Esegui(
            "CREATE LOGIN [ACME\\mario.rossi] FROM WINDOWS;\nEXEC dbo.usp_Chiudi @Operatore = N'ACME\\mario.rossi';", "Acme");

        Assert.Contains("CREATE LOGIN [dom_01\\usr_001] FROM WINDOWS;", testo);
        Assert.Contains("@Operatore = N'dom_01\\usr_001'", testo);
        Assert.Equal(
            "CREATE LOGIN [ACME\\mario.rossi] FROM WINDOWS;\nEXEC dbo.usp_Chiudi @Operatore = N'ACME\\mario.rossi';",
            Aiuti.RipristinaPerProva(testo, mappa).Replace("\r", ""));
    }

    [Fact]
    public void I_percorsi_e_i_nomi_gia_anonimi_non_si_ritoccano()
    {
        var (testo, _, _) = Esegui("BACKUP DATABASE Vendite TO DISK = N'\\\\SRVBK01\\Condivisa\\Vendite_FULL.bak';", "Acme");

        Assert.Equal("BACKUP DATABASE db_01 TO DISK = N'\\\\srv_01\\dir_001\\db_01_FULL.bak';", testo);
    }

    [Fact]
    public void Con_l_interruttore_spento_le_stringhe_restano_come_sono_e_l_anti_fuga_le_segnala()
    {
        var mappa = new MappaCliente();
        var profilo = new Profilo { Nome = "Prova", Blacklist = ["Acme"], NomiNelleStringhe = false };
        const string sql = "PRINT N'Cliente Acme';";

        var risultato = AnonimizzatoreScript.Anonimizza([sql], profilo, mappa);
        var esito = ControlloAntiFuga.Verifica([sql], risultato, mappa, profilo);

        Assert.Equal(sql, risultato.Script[0].Testo);
        Assert.Contains(esito.Fughe, f => f.Gravita == GravitaFuga.Bloccante);
    }

    [Fact]
    public void Dopo_la_sostituzione_l_anti_fuga_non_trova_piu_nulla()
    {
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo("Acme");
        const string sql = "SELECT * FROM dbo.AcmeOrdini WHERE Nota LIKE N'%Acme%';\nPRINT N'Ordini AcmeVendite pronti';";

        var risultato = AnonimizzatoreScript.Anonimizza([sql], profilo, mappa);
        var esito = ControlloAntiFuga.Verifica([sql], risultato, mappa, profilo);

        Assert.True(esito.Pulito, string.Join("; ", esito.Fughe.Select(f => f.Messaggio)));
    }

    [Fact]
    public void Il_termine_ha_un_nome_stabile_tra_una_chiamata_e_l_altra()
    {
        var mappa = new MappaCliente();
        var profilo = Aiuti.NuovoProfilo("Acme");

        var primo = Aiuti.Anon("PRINT N'Acme uno';", mappa, profilo);
        var secondo = Aiuti.Anon("PRINT N'due Acme';", mappa, profilo);

        Assert.Equal("PRINT N'txt_001 uno';", primo);
        Assert.Equal("PRINT N'due txt_001';", secondo);
    }

    [Fact]
    public void L_interruttore_e_acceso_di_default_e_si_salva_nel_profilo()
    {
        Assert.True(new Profilo { Nome = "x" }.NomiNelleStringhe);

        var cartella = Path.Combine(Path.GetTempPath(), "anonimizzatore-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archivio = new ArchivioProfili(cartella, new ProtezioneNulla());
            var profilo = new Profilo { Nome = "Prova", NomiNelleStringhe = false };
            archivio.Salva(profilo, new MappaCliente());

            Assert.False(archivio.Carica(profilo.Id).Profilo.NomiNelleStringhe);
        }
        finally
        {
            if (Directory.Exists(cartella)) Directory.Delete(cartella, true);
        }
    }

    [Fact]
    public void La_legenda_spiega_txt_solo_se_c_e()
    {
        var (testo, mappa, profilo) = Esegui("PRINT N'Cliente Acme';", "Acme");
        var risultato = new RisultatoAnonimizzazione([new ScriptAnonimizzato(testo, [])]);

        var unico = TestoPerIA.Componi(risultato, mappa, null, profilo);

        Assert.Contains("txt_001", unico.Testo);
    }

    private sealed class ProtezioneNulla : IProtezioneDati
    {
        public byte[] Proteggi(byte[] dati) => dati;
        public byte[] Sblocca(byte[] datiProtetti) => datiProtetti;
    }
}
