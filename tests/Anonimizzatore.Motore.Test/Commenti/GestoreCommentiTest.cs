using Anonimizzatore.Motore.Commenti;
using Anonimizzatore.Motore.Lettura;

namespace Anonimizzatore.Motore.Test.Commenti;

public class GestoreCommentiTest
{
    private static string LeggiEsempio(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Esempi", nome));

    // ---------- Anonimizza ----------

    [Fact]
    public void Commento_su_riga_singola_diventa_segnaposto_e_l_a_capo_resta()
    {
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza("SELECT 1 -- numero uno\nSELECT 2");

        Assert.Equal("SELECT 1 /* c001 */\nSELECT 2", anonimo);
        Assert.Equal("-- numero uno", Assert.Single(g.Commenti).Testo);
    }

    [Fact]
    public void Commento_con_a_capo_windows_non_mangia_il_ritorno_carrello()
    {
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza("SELECT 1 -- uno\r\nSELECT 2\r\n");

        Assert.Equal("SELECT 1 /* c001 */\r\nSELECT 2\r\n", anonimo);
        Assert.Equal("-- uno", g.Commenti[0].Testo);
    }

    [Fact]
    public void Commento_finale_senza_a_capo_viene_sostituito()
    {
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza("SELECT 1 -- fine");

        Assert.Equal("SELECT 1 /* c001 */", anonimo);
        Assert.Equal("-- fine", g.Commenti[0].Testo);
    }

    [Fact]
    public void Commento_multilinea_diventa_un_solo_segnaposto()
    {
        var g = new GestoreCommenti();
        var testo = "/*\n  Procedura di prova\n  Autore: inventato\n*/\nSELECT 1";

        var anonimo = g.Anonimizza(testo);

        Assert.Equal("/* c001 */\nSELECT 1", anonimo);
        Assert.Equal("/*\n  Procedura di prova\n  Autore: inventato\n*/", g.Commenti[0].Testo);
    }

    [Fact]
    public void Commenti_annidati_sono_un_solo_commento()
    {
        // In T-SQL i /* */ si annidano: il commento finisce solo all'ultimo */.
        var g = new GestoreCommenti();
        var testo = "SELECT 1 /* esterno /* interno */ ancora esterno */ , 2";

        var anonimo = g.Anonimizza(testo);

        Assert.Equal("SELECT 1 /* c001 */ , 2", anonimo);
        Assert.Equal("/* esterno /* interno */ ancora esterno */", g.Commenti[0].Testo);
        Assert.Equal(testo, g.Ripristina(anonimo).Testo);
    }

    [Fact]
    public void Trattini_dentro_le_stringhe_non_sono_commenti()
    {
        var g = new GestoreCommenti();
        var testo = "SELECT '-- non un commento', '/* nemmeno questo */' -- questo sì";

        var anonimo = g.Anonimizza(testo);

        Assert.Equal("SELECT '-- non un commento', '/* nemmeno questo */' /* c001 */", anonimo);
        Assert.Single(g.Commenti);
    }

    [Fact]
    public void Trattini_dentro_un_nome_tra_parentesi_quadre_non_sono_commenti()
    {
        var g = new GestoreCommenti();
        var testo = "SELECT [a--b] FROM [x/*y*/z]";

        Assert.Equal(testo, g.Anonimizza(testo));
        Assert.Empty(g.Commenti);
    }

    [Fact]
    public void Numerazione_in_ordine_e_continua_tra_piu_script()
    {
        var g = new GestoreCommenti();

        var a = g.Anonimizza("-- uno\nSELECT 1 /* due */");
        var b = g.Anonimizza("SELECT 2 -- tre");

        Assert.Equal("/* c001 */\nSELECT 1 /* c002 */", a);
        Assert.Equal("SELECT 2 /* c003 */", b);
        Assert.Equal([1, 2, 3], g.Commenti.Select(c => c.Numero));
    }

    [Fact]
    public void Testo_senza_commenti_resta_identico()
    {
        var g = new GestoreCommenti();
        var testo = "SELECT  a,\t b\r\n  FROM   dbo.Tabella   WHERE a = 1;\n\n";

        Assert.Equal(testo, g.Anonimizza(testo));
        Assert.Empty(g.Commenti);
    }

    [Fact]
    public void Testo_vuoto_e_solo_spazi_restano_identici()
    {
        var g = new GestoreCommenti();

        Assert.Equal("", g.Anonimizza(""));
        Assert.Equal("  \r\n ", g.Anonimizza("  \r\n "));
    }

    [Fact]
    public void Funziona_anche_su_uno_script_gia_letto()
    {
        var g = new GestoreCommenti();
        var letto = LettoreScript.Leggi("SELECT 1 -- x");

        Assert.Equal("SELECT 1 /* c001 */", g.Anonimizza(letto));
    }

    [Fact]
    public void Funziona_su_un_frammento_di_sql_dinamico_incompleto()
    {
        // Testo estratto da una stringa: non è uno script valido, ma i commenti si riconoscono lo stesso.
        var g = new GestoreCommenti();
        var frammento = "SELECT * FROM dbo.Tabella WHERE -- filtro\n  Stato = ";

        var anonimo = g.Anonimizza(frammento);

        Assert.Equal("SELECT * FROM dbo.Tabella WHERE /* c001 */\n  Stato = ", anonimo);
        Assert.Equal(frammento, g.Ripristina(anonimo).Testo);
    }

    [Fact]
    public void Commento_multilinea_mai_chiuso_viene_comunque_sostituito()
    {
        // Meglio un segnaposto che lasciare in chiaro un testo che non si può analizzare.
        var g = new GestoreCommenti();
        var testo = "SELECT 1 /* commento mai chiuso";

        var anonimo = g.Anonimizza(testo);

        Assert.DoesNotContain("commento", anonimo);
        Assert.Equal(testo, g.Ripristina(anonimo).Testo);
    }

    [Fact]
    public void Un_commento_che_somiglia_a_un_segnaposto_viene_sostituito_come_gli_altri()
    {
        var g = new GestoreCommenti();
        var testo = "SELECT 1 /* c001 */ -- altro";

        var anonimo = g.Anonimizza(testo);

        Assert.Equal("SELECT 1 /* c001 */ /* c002 */", anonimo);
        Assert.Equal(testo, g.Ripristina(anonimo).Testo);
    }

    // ---------- Ripristina ----------

    [Fact]
    public void Ripristina_rimette_i_commenti_originali()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("SELECT 1 -- uno\nSELECT 2 /* due */");

        var r = g.Ripristina("SELECT 1 /* c001 */\nSELECT 2 /* c002 */");

        Assert.Equal("SELECT 1 -- uno\nSELECT 2 /* due */", r.Testo);
        Assert.Empty(r.Persi);
        Assert.Empty(r.Sconosciuti);
    }

    [Theory]
    [InlineData("/*c001*/")]
    [InlineData("/* C001 */")]
    [InlineData("/*   c001   */")]
    [InlineData("/*\tc001\t*/")]
    [InlineData("/* c1 */")]
    [InlineData("/* c0001 */")]
    [InlineData("/* c00001 */")]
    public void Ripristina_riconosce_i_segnaposto_rimaneggiati_dall_IA(string segnaposto)
    {
        var g = new GestoreCommenti();
        g.Anonimizza("SELECT 1 /* originale */");

        var r = g.Ripristina($"SELECT 2 {segnaposto}");

        Assert.Equal("SELECT 2 /* originale */", r.Testo);
        Assert.Empty(r.Persi);
    }

    [Fact]
    public void Ripristina_distingue_numeri_con_piu_cifre()
    {
        var g = new GestoreCommenti();
        var righe = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"SELECT {i} -- riga {i}"));
        var anonimo = g.Anonimizza(righe);

        Assert.Contains("/* c012 */", anonimo);
        Assert.Equal(righe, g.Ripristina(anonimo).Testo);
    }

    [Fact]
    public void Ripristina_segnala_i_segnaposto_persi()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("-- a\nSELECT 1 -- b\n/* c */ SELECT 2");

        var r = g.Ripristina("/* c001 */\nSELECT 1\nSELECT 2 /* c003 */");

        Assert.Equal([2], r.Persi);
        Assert.Equal(["/* c002 */"], r.PersiComeSegnaposto);
        Assert.Equal("-- a\nSELECT 1\nSELECT 2 /* c */", r.Testo);
    }

    [Fact]
    public void Ripristina_segnala_i_segnaposto_sconosciuti_e_li_lascia_come_sono()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("/* a */");

        var r = g.Ripristina("SELECT 1 /* c001 */ /* c077 */");

        Assert.Equal("SELECT 1 /* a */ /* c077 */", r.Testo);
        Assert.Equal([77], r.Sconosciuti);
    }

    [Fact]
    public void Segnaposto_di_commento_riga_spostato_a_meta_riga_non_mangia_il_codice()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("-- nota");

        var r = g.Ripristina("SELECT /* c001 */ 1, 2");

        Assert.Equal("SELECT -- nota\n 1, 2", r.Testo);
        Assert.Equal([1], r.Spostati);
    }

    [Fact]
    public void Segnaposto_spostato_mantiene_l_a_capo_windows_del_testo()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("-- nota");

        var r = g.Ripristina("SELECT 1 /* c001 */ , 2\r\nFROM t");

        Assert.Equal("SELECT 1 -- nota\r\n , 2\r\nFROM t", r.Testo);
    }

    [Fact]
    public void Segnaposto_di_commento_riga_a_fine_riga_o_a_fine_testo_non_aggiunge_nulla()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("-- nota");

        Assert.Equal("SELECT 1 -- nota", g.Ripristina("SELECT 1 /* c001 */").Testo);
        Assert.Equal("SELECT 1 -- nota  \nx", g.Ripristina("SELECT 1 /* c001 */  \nx").Testo);
        Assert.Empty(g.Ripristina("SELECT 1 /* c001 */").Spostati);
    }

    [Fact]
    public void Ripristina_non_rielabora_il_testo_ripristinato()
    {
        // Il commento originale contiene qualcosa che somiglia a un segnaposto: deve restare com'è.
        var g = new GestoreCommenti();
        g.Anonimizza("SELECT 1 /* vedi /* c002 */ qui */\nSELECT 2 /* due */");

        var r = g.Ripristina("SELECT 1 /* c001 */\nSELECT 2 /* c002 */");

        Assert.Equal("SELECT 1 /* vedi /* c002 */ qui */\nSELECT 2 /* due */", r.Testo);
    }

    [Fact]
    public void Segnaposto_ripetuto_dall_IA_viene_ripristinato_ovunque()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("/* nota */ SELECT 1");

        var r = g.Ripristina("/* c001 */ A\n/* c001 */ B");

        Assert.Equal("/* nota */ A\n/* nota */ B", r.Testo);
        Assert.Empty(r.Persi);
    }

    [Fact]
    public void Si_puo_ripristinare_in_un_secondo_momento_su_un_altro_testo()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("SELECT 1 -- primo\nSELECT 2 -- secondo");

        // Due risposte diverse dell'IA, ripristinate una dopo l'altra con la stessa istanza.
        var r1 = g.Ripristina("SELECT 1 /* c001 */");
        var r2 = g.Ripristina("SELECT 2 /* c002 */");

        Assert.Equal("SELECT 1 -- primo", r1.Testo);
        Assert.Equal([2], r1.Persi);
        Assert.Equal("SELECT 2 -- secondo", r2.Testo);
        Assert.Equal([1], r2.Persi);
    }

    // ---------- Round-trip ----------

    [Theory]
    [InlineData("")]
    [InlineData("SELECT 1")]
    [InlineData("-- solo un commento")]
    [InlineData("SELECT 1 -- fine senza a capo")]
    [InlineData("SELECT 1 -- con a capo\r\nSELECT 2\r\n")]
    [InlineData("/* a */ /* b */ /* c */")]
    [InlineData("SELECT /* in mezzo */ 1")]
    [InlineData("SELECT '--x' + '/*y*/' -- z")]
    [InlineData("/* a /* b */ c */ SELECT 1")]
    [InlineData("--\n--\n--")]
    [InlineData("/**/SELECT/***/1")]
    [InlineData("SELECT 1 -- commento con 'apici' e \"virgolette\" e */ chiusura\nGO\n")]
    [InlineData("SELECT 1;\n\n\n   -- indentato\n\t/* tab */\n")]
    public void Andata_e_ritorno_restituisce_il_testo_identico(string testo)
    {
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza(testo);
        var r = g.Ripristina(anonimo);

        Assert.Equal(testo, r.Testo);
        Assert.Empty(r.Persi);
        Assert.Empty(r.Sconosciuti);
        Assert.Empty(r.Spostati);
    }

    [Fact]
    public void Andata_e_ritorno_sullo_script_di_esempio()
    {
        var testo = LeggiEsempio("vendite-ordini.sql");
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza(testo);

        Assert.Equal(3, g.Commenti.Count);
        Assert.Matches(@"/\* c001 \*/", anonimo);
        Assert.Equal(testo, g.Ripristina(anonimo).Testo);
    }

    [Fact]
    public void Dopo_l_anonimizzazione_nessun_testo_dei_commenti_resta_nell_output()
    {
        var g = new GestoreCommenti();

        var anonimo = g.Anonimizza("SELECT 1 -- segreto-uno\n/* segreto-due\n   su più righe */\nSELECT 2");

        Assert.DoesNotContain("segreto", anonimo);
    }

    // ---------- Serializzazione ----------

    [Fact]
    public void Lo_stato_si_salva_in_json_e_si_ricarica()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("SELECT 1 -- uno\n/* due\ndue */");

        var json = g.ToJson();
        var ricaricato = GestoreCommenti.DaJson(json);

        Assert.Equal(g.Commenti, ricaricato.Commenti);
        Assert.Equal("SELECT 1 -- uno\n/* due\ndue */", ricaricato.Ripristina("SELECT 1 /* c001 */\n/* c002 */").Testo);
    }

    [Fact]
    public void Dopo_il_ricaricamento_la_numerazione_prosegue()
    {
        var g = GestoreCommenti.DaJson(Crea("-- a", "-- b").ToJson());

        var anonimo = g.Anonimizza("SELECT 1 -- c");

        Assert.Equal("SELECT 1 /* c003 */", anonimo);
        Assert.Equal(3, g.Commenti.Count);
    }

    [Fact]
    public void Il_json_contiene_il_testo_dei_commenti_con_caratteri_speciali_intatti()
    {
        var g = new GestoreCommenti();
        g.Anonimizza("-- àèì \"virgolette\" \\ barra\nSELECT 1");

        var ricaricato = GestoreCommenti.DaJson(g.ToJson());

        Assert.Equal("-- àèì \"virgolette\" \\ barra", ricaricato.Commenti[0].Testo);
    }

    [Fact]
    public void Rigenera_azzera_commenti_e_numerazione()
    {
        var g = Crea("-- a", "-- b");

        g.Rigenera();

        Assert.Empty(g.Commenti);
        Assert.Equal("SELECT 1 /* c001 */", g.Anonimizza("SELECT 1 -- nuovo"));
    }

    private static GestoreCommenti Crea(params string[] commentiRiga)
    {
        var g = new GestoreCommenti();
        g.Anonimizza(string.Join("\n", commentiRiga));
        return g;
    }
}
