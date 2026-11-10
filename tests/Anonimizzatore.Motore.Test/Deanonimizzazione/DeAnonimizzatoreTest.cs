using Anonimizzatore.Motore.Deanonimizzazione;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Test.Deanonimizzazione;

// Tutti i nomi sono inventati.
public class DeAnonimizzatoreTest
{
    private static (MappaCliente Mappa, DeAnonimizzatore De) Crea()
    {
        var mappa = new MappaCliente();
        return (mappa, new DeAnonimizzatore(mappa));
    }

    private static string Esegui(MappaCliente mappa, string testo) =>
        new DeAnonimizzatore(mappa).DeAnonimizza(testo).Testo;

    [Fact]
    public void Sostituisce_un_nome_semplice()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        var r = de.DeAnonimizza("SELECT * FROM tbl_001");

        Assert.Equal("SELECT * FROM Ordini", r.Testo);
        Assert.Equal(1, r.Sostituzioni);
        Assert.Empty(r.NonTrovati);
    }

    [Fact]
    public void Testo_senza_nomi_anonimi_resta_identico()
    {
        var (_, de) = Crea();
        const string testo = "Ciao, usa un indice sulla colonna Data.\nSELECT 1; -- ok [x] \"y\"";

        var r = de.DeAnonimizza(testo);

        Assert.Equal(testo, r.Testo);
        Assert.Equal(0, r.Sostituzioni);
    }

    [Fact]
    public void Testo_vuoto()
    {
        var (_, de) = Crea();
        Assert.Equal("", de.DeAnonimizza("").Testo);
    }

    [Theory]
    [InlineData("TBL_001")]
    [InlineData("Tbl_001")]
    [InlineData("tbl_001")]
    public void Ignora_maiuscole_e_minuscole(string scritto)
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("Ordini", de.DeAnonimizza(scritto).Testo);
    }

    [Fact]
    public void Tutte_le_famiglie_di_oggetti()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Server, "SRVPROD");
        mappa.Ottieni(Ruolo.Database, "Magazzino");
        mappa.Ottieni(Ruolo.Schema, "Vendite");
        mappa.Ottieni(Ruolo.Vista, "VistaOrdini");
        mappa.Ottieni(Ruolo.Procedura, "CalcolaTotale");
        mappa.Ottieni(Ruolo.Funzione, "Sconto");
        mappa.Ottieni(Ruolo.Trigger, "AggiornaData");
        mappa.Ottieni(Ruolo.Job, "NotturnoBackup");
        mappa.Ottieni(Ruolo.Utente, "mario");
        mappa.Ottieni(Ruolo.RuoloDb, "Lettori");
        mappa.Ottieni(Ruolo.Dominio, "ACME");
        mappa.Ottieni(Ruolo.Indice, "IX_Data");
        mappa.Ottieni(Ruolo.PrimaryKey, "PK_Ordini");
        mappa.Ottieni(Ruolo.ForeignKey, "FK_Ordini_Clienti");
        mappa.Ottieni(Ruolo.Default, "DF_Ordini_Data");
        mappa.Ottieni(Ruolo.Check, "CK_Importo");
        mappa.Ottieni(Ruolo.Unique, "UQ_Codice");

        var testo = "srv_01 db_01 sch_01 vw_001 usp_001 fn_001 trg_001 job_001 usr_001 rol_001 dom_01 " +
                    "ix_001 pk_001 fk_001 df_001 ck_001 uq_001";

        Assert.Equal(
            "SRVPROD Magazzino Vendite VistaOrdini CalcolaTotale Sconto AggiornaData NotturnoBackup mario Lettori " +
            "ACME IX_Data PK_Ordini FK_Ordini_Clienti DF_Ordini_Data CK_Importo UQ_Codice",
            de.DeAnonimizza(testo).Testo);
    }

    [Fact]
    public void Nomi_con_parentesi_quadre_le_mantengono()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Schema, "Vendite");
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("SELECT * FROM [Vendite].[Ordini]", de.DeAnonimizza("SELECT * FROM [sch_01].[tbl_001]").Testo);
        Assert.Equal("[Ordini]", de.DeAnonimizza("[TBL_001]").Testo);
    }

    [Fact]
    public void Nomi_da_quotare_ricevono_le_parentesi_se_mancano()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini Cliente");
        mappa.Ottieni(Ruolo.Tabella, "Dati-2020");
        mappa.Ottieni(Ruolo.Tabella, "Order");
        mappa.Ottieni(Ruolo.Tabella, "2020Dati");

        Assert.Equal("FROM [Ordini Cliente]", de.DeAnonimizza("FROM tbl_001").Testo);
        Assert.Equal("[Dati-2020]", de.DeAnonimizza("tbl_002").Testo);
        Assert.Equal("dbo.[Order]", de.DeAnonimizza("dbo.tbl_003").Testo);
        Assert.Equal("[2020Dati]", de.DeAnonimizza("tbl_004").Testo);
    }

    [Fact]
    public void Nomi_da_quotare_gia_tra_parentesi_non_hanno_doppie_parentesi()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini Cliente");

        Assert.Equal("FROM [Ordini Cliente]", de.DeAnonimizza("FROM [tbl_001]").Testo);
    }

    [Fact]
    public void Parentesi_chiuse_nel_nome_originale_vengono_raddoppiate()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "[Strana]]Tabella]");

        Assert.Equal("[Strana]]Tabella]", de.DeAnonimizza("tbl_001").Testo);
        Assert.Equal("[Strana]]Tabella]", de.DeAnonimizza("[tbl_001]").Testo);
    }

    [Fact]
    public void Tra_doppi_apici_non_aggiunge_parentesi()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini Cliente");

        Assert.Equal("FROM \"Ordini Cliente\"", de.DeAnonimizza("FROM \"tbl_001\"").Testo);
    }

    [Fact]
    public void Dominio_e_utente_dentro_la_stessa_parentesi()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Dominio, "ACME");
        mappa.Ottieni(Ruolo.Utente, "mario.rossi");

        Assert.Equal("CREATE LOGIN [ACME\\mario.rossi] FROM WINDOWS",
            de.DeAnonimizza("CREATE LOGIN [dom_01\\usr_001] FROM WINDOWS").Testo);
    }

    [Theory]
    [InlineData("FK_tbl_001_tbl_002", "FK_Ordini_Clienti")]
    [InlineData("IX_tbl_002_Data", "IX_Clienti_Data")]
    [InlineData("fk_tbl_001_tbl_002_v2", "fk_Ordini_Clienti_v2")]
    [InlineData("dbo.tbl_001_storico", "dbo.Ordini_storico")]
    [InlineData("vista_di_tbl_001", "vista_di_Ordini")]
    public void Nomi_composti_inventati_dall_IA(string scritto, string atteso)
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Tabella, "Clienti");

        Assert.Equal(atteso, de.DeAnonimizza(scritto).Testo);
    }

    [Fact]
    public void Nome_composto_con_originale_da_quotare_mette_le_parentesi_a_tutto_il_nome()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini Cliente");

        Assert.Equal("ADD CONSTRAINT [FK_Ordini Cliente_x]",
            de.DeAnonimizza("ADD CONSTRAINT FK_tbl_001_x").Testo);
        Assert.Equal("[FK_Ordini Cliente_x]", de.DeAnonimizza("[FK_tbl_001_x]").Testo);
    }

    [Theory]
    [InlineData("tbl_0012")]
    [InlineData("tbl_0010")]
    [InlineData("xtbl_001")]
    [InlineData("mytbl_001")]
    [InlineData("tbl_001a")]
    [InlineData("tbl_001x_y")]
    [InlineData("tbl_00")]
    [InlineData("tbl_")]
    [InlineData("tbl001")]
    [InlineData("tbl-001")]
    [InlineData("@tbl_001")]
    [InlineData("1tbl_001")]
    public void Niente_falsi_positivi_dentro_parole_piu_lunghe(string scritto)
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        var r = de.DeAnonimizza(scritto);

        Assert.DoesNotContain("Ordini", r.Testo);
        Assert.Equal(scritto, r.Testo);
        Assert.Equal(0, r.Sostituzioni);
    }

    [Fact]
    public void Numero_a_quattro_cifre_viene_trovato_solo_se_in_mappa()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        var r = de.DeAnonimizza("tbl_0012 e tbl_001");

        Assert.Equal("tbl_0012 e Ordini", r.Testo);
        // tbl_0012 non è tbl_001; ha la forma di un nome anonimo (numero 12), ma non esiste in mappa.
        Assert.Equal(1, r.Sostituzioni);
    }

    [Fact]
    public void Punteggiatura_attorno_ai_nomi()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("(Ordini), Ordini; Ordini. 'Ordini' `Ordini` \"Ordini\"",
            de.DeAnonimizza("(tbl_001), tbl_001; tbl_001. 'tbl_001' `tbl_001` \"tbl_001\"").Testo);
    }

    [Fact]
    public void Nome_con_punto_nella_frase_non_confonde_il_confine()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("Controlla la tabella Ordini.\nPoi Ordini.Id", de.DeAnonimizza("Controlla la tabella tbl_001.\nPoi tbl_001.Id").Testo);
    }

    [Fact]
    public void Colonne_si_risolvono_dal_solo_numero()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Colonna, "Nome", "varchar(50)");
        mappa.Ottieni(Ruolo.Colonna, "Totale", "decimal(18,2)");
        mappa.Ottieni(Ruolo.Colonna, "Misterioso");

        Assert.Equal("Nome", de.DeAnonimizza("col_vc50_001").Testo);
        Assert.Equal("Nome", de.DeAnonimizza("col_int_001").Testo);
        Assert.Equal("Nome", de.DeAnonimizza("COL_X_001").Testo);
        Assert.Equal("Totale", de.DeAnonimizza("col_dec18_2_002").Testo);
        Assert.Equal("Totale", de.DeAnonimizza("col_dt_002").Testo);
        Assert.Equal("Misterioso", de.DeAnonimizza("col_x_003").Testo);
        Assert.Equal("Misterioso", de.DeAnonimizza("col_nvcmax_003").Testo);
    }

    [Fact]
    public void Colonna_nel_confronto_non_si_confonde_con_scale_o_nomi_composti()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Colonna, "Nome", "varchar(50)");
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        Assert.Equal("idx_Nome_Ordini", de.DeAnonimizza("idx_col_vc50_001_tbl_001").Testo);
        Assert.Equal("Ordini.Nome", de.DeAnonimizza("tbl_001.col_vc50_001").Testo);
        Assert.Equal("Ordini.[Nome]", de.DeAnonimizza("tbl_001.[col_vc50_001]").Testo);
    }

    [Fact]
    public void Colonna_con_numero_errato_o_lungo_non_viene_scambiata()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Colonna, "Nome", "varchar(50)");

        var r = de.DeAnonimizza("col_vc50_0012 col_vc50_002 col_vc50_001x");

        Assert.Equal("col_vc50_0012 col_vc50_002 col_vc50_001x", r.Testo);
        Assert.Equal(2, r.NonTrovati.Count);
    }

    [Fact]
    public void Variabili_e_parametri()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Parametro, "@IdCliente", "int");
        mappa.Ottieni(Ruolo.Variabile, "@Totale", "decimal(18,2)");

        Assert.Equal("@IdCliente", de.DeAnonimizza("@p_int_001").Testo);
        Assert.Equal("@IdCliente", de.DeAnonimizza("@P_VC20_001").Testo);
        Assert.Equal("@IdCliente", de.DeAnonimizza("@v_int_001").Testo);
        Assert.Equal("@Totale", de.DeAnonimizza("@v_dec18_2_002").Testo);
        Assert.Equal("WHERE Id = @IdCliente AND T > @Totale",
            de.DeAnonimizza("WHERE Id = @p_int_001 AND T > @v_money_002").Testo);
    }

    [Fact]
    public void Variabili_non_quotate_con_parentesi_quadre()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Variabile, "@Mio Totale", "int");

        // Una variabile non si quota mai con le parentesi.
        Assert.Equal("@Mio Totale", de.DeAnonimizza("@v_int_001").Testo);
    }

    [Fact]
    public void Tabelle_temporanee()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Temporanea, "#Appoggio");
        mappa.Ottieni(Ruolo.Temporanea, "##Globale");

        Assert.Equal("SELECT * FROM #Appoggio", de.DeAnonimizza("SELECT * FROM #tmp_001").Testo);
        Assert.Equal("##Globale", de.DeAnonimizza("##TMP_002").Testo);
        Assert.Equal("#Appoggio", de.DeAnonimizza("[#tmp_001]").Testo.Replace("[", "").Replace("]", ""));
    }

    [Fact]
    public void Tmp_senza_cancelletto_non_e_una_temporanea()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Temporanea, "#Appoggio");

        Assert.Equal("tmp_001", de.DeAnonimizza("tmp_001").Testo);
    }

    [Fact]
    public void Segnala_i_token_senza_corrispondenza_e_li_lascia_invariati()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        const string testo = "FROM tbl_001 JOIN tbl_099 ON usp_005 = srv_07";

        var r = de.DeAnonimizza(testo);

        Assert.Equal("FROM Ordini JOIN tbl_099 ON usp_005 = srv_07", r.Testo);
        Assert.Equal(1, r.Sostituzioni);
        Assert.Collection(r.NonTrovati,
            t => { Assert.Equal("tbl_099", t.Token); Assert.Equal(testo.IndexOf("tbl_099", StringComparison.Ordinal), t.Posizione); },
            t => Assert.Equal("usp_005", t.Token),
            t => Assert.Equal("srv_07", t.Token));
    }

    [Fact]
    public void Posizione_del_token_non_trovato_dentro_parentesi_e_nomi_composti()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        const string testo = "x [tbl_050] FK_tbl_060_y";

        var r = de.DeAnonimizza(testo);

        Assert.Equal(testo, r.Testo);
        Assert.Equal(testo.IndexOf("tbl_050", StringComparison.Ordinal), r.NonTrovati[0].Posizione);
        Assert.Equal(testo.IndexOf("tbl_060", StringComparison.Ordinal), r.NonTrovati[1].Posizione);
    }

    [Fact]
    public void Le_posizioni_sono_riferite_al_testo_di_partenza_anche_dopo_sostituzioni_precedenti()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "UnNomeMoltoLungoDiTabella");
        const string testo = "tbl_001 tbl_002";

        var r = de.DeAnonimizza(testo);

        Assert.Equal(8, r.NonTrovati.Single().Posizione);
    }

    [Fact]
    public void I_segnaposto_dei_commenti_non_sono_toccati()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");

        var r = de.DeAnonimizza("/* c001 */ SELECT * FROM tbl_001 /* c002 */");

        Assert.Equal("/* c001 */ SELECT * FROM Ordini /* c002 */", r.Testo);
        Assert.Empty(r.NonTrovati);
    }

    [Fact]
    public void La_stessa_mappa_serve_piu_volte_e_gli_originali_mantengono_la_forma()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "OrdiniClienti");

        // L'originale è restituito come registrato, qualunque sia il caso scritto dall'IA.
        Assert.Equal("OrdiniClienti OrdiniClienti", de.DeAnonimizza("TBL_001 tbl_001").Testo);
    }

    [Fact]
    public void Risposta_dell_IA_con_prosa_e_codice()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Tabella, "Clienti");
        mappa.Ottieni(Ruolo.Colonna, "IdCliente", "int");
        mappa.Ottieni(Ruolo.Colonna, "Totale", "decimal(18,2)");
        mappa.Ottieni(Ruolo.Parametro, "@Anno", "int");

        const string risposta = """
            La query è lenta perché `tbl_001` non ha un indice su **col_int_001**.
            Prova così:

            ```sql
            CREATE INDEX IX_tbl_001_col_int_001 ON dbo.tbl_001 (col_int_001) INCLUDE (col_dec18_2_002);

            SELECT c.col_int_001, SUM(o.col_dec18_2_002)
            FROM dbo.tbl_001 AS o
            JOIN dbo.tbl_002 AS c ON c.col_int_001 = o.col_int_001
            WHERE YEAR(o.col_dt_009) = @p_int_001
            GROUP BY c.col_int_001;
            ```

            Nota: tbl_002 ha pochi record.
            """;

        var r = de.DeAnonimizza(risposta);

        Assert.Contains("`Ordini` non ha un indice su **IdCliente**", r.Testo);
        Assert.Contains("CREATE INDEX IX_Ordini_IdCliente ON dbo.Ordini (IdCliente) INCLUDE (Totale);", r.Testo);
        Assert.Contains("JOIN dbo.Clienti AS c ON c.IdCliente = o.IdCliente", r.Testo);
        Assert.Contains("= @Anno", r.Testo);
        Assert.Contains("Nota: Clienti ha pochi record.", r.Testo);
        Assert.Equal("col_dt_009", r.NonTrovati.Single().Token);
    }

    // ----- round-trip -----

    [Fact]
    public void Andata_e_ritorno_di_uno_script_con_nomi_di_ogni_famiglia()
    {
        var mappa = new MappaCliente();
        var originale = new Dictionary<string, string>
        {
            [mappa.Ottieni(Ruolo.Server, "SRVPROD01")] = "SRVPROD01",
            [mappa.Ottieni(Ruolo.Database, "Magazzino")] = "Magazzino",
            [mappa.Ottieni(Ruolo.Schema, "Vendite")] = "Vendite",
            [mappa.Ottieni(Ruolo.Tabella, "Ordini")] = "Ordini",
            [mappa.Ottieni(Ruolo.Colonna, "IdOrdine", "int")] = "IdOrdine",
            [mappa.Ottieni(Ruolo.Colonna, "Descrizione", "nvarchar(max)")] = "Descrizione",
            [mappa.Ottieni(Ruolo.Parametro, "@DaData", "datetime")] = "@DaData",
            [mappa.Ottieni(Ruolo.Variabile, "@Conteggio", "int")] = "@Conteggio",
            [mappa.Ottieni(Ruolo.Temporanea, "#Appoggio")] = "#Appoggio",
        };
        Assert.Equal(9, originale.Count);

        // Lo script anonimizzato, come lo restituirebbe il motore.
        var anonimo =
            $"USE {mappa.Ottieni(Ruolo.Database, "Magazzino")};\n" +
            $"SELECT {mappa.Ottieni(Ruolo.Colonna, "IdOrdine", "int")}, {mappa.Ottieni(Ruolo.Colonna, "Descrizione")}\n" +
            $"INTO {mappa.Ottieni(Ruolo.Temporanea, "#Appoggio")}\n" +
            $"FROM [{mappa.Ottieni(Ruolo.Server, "SRVPROD01")}].[{mappa.Ottieni(Ruolo.Database, "Magazzino")}]." +
            $"[{mappa.Ottieni(Ruolo.Schema, "Vendite")}].{mappa.Ottieni(Ruolo.Tabella, "Ordini")}\n" +
            $"WHERE x > {mappa.Ottieni(Ruolo.Parametro, "@DaData")} AND y = {mappa.Ottieni(Ruolo.Variabile, "@Conteggio")};";

        var atteso =
            "USE Magazzino;\n" +
            "SELECT IdOrdine, Descrizione\n" +
            "INTO #Appoggio\n" +
            "FROM [SRVPROD01].[Magazzino].[Vendite].Ordini\n" +
            "WHERE x > @DaData AND y = @Conteggio;";

        var r = new DeAnonimizzatore(mappa).DeAnonimizza(anonimo);

        Assert.Equal(atteso, r.Testo);
        Assert.Empty(r.NonTrovati);
        Assert.DoesNotContain("tbl_", r.Testo);
        Assert.DoesNotContain("col_", r.Testo);
    }

    [Theory]
    [InlineData("Ordini")]
    [InlineData("Ordini Clienti")]
    [InlineData("Dati.Storico")]
    [InlineData("Società-Ñandú")]
    [InlineData("Order")]
    public void Round_trip_con_nomi_da_quotare(string nome)
    {
        var mappa = new MappaCliente();
        var anonimo = mappa.Ottieni(Ruolo.Tabella, nome);

        var r = Esegui(mappa, $"SELECT * FROM {anonimo}");
        var conParentesi = Esegui(mappa, $"SELECT * FROM [{anonimo}]");

        // Il risultato deve poter essere rimesso in un SELECT: o il nome è regolare, o è tra parentesi.
        Assert.True(r == $"SELECT * FROM {nome}" || r == $"SELECT * FROM [{nome}]", r);
        Assert.True(conParentesi == $"SELECT * FROM [{nome}]", conParentesi);
    }

    [Fact]
    public void Round_trip_con_la_mappa_salvata_e_ricaricata()
    {
        var mappa = new MappaCliente();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Colonna, "Totale", "decimal(18,2)");
        var ricaricata = MappaCliente.Da(mappa.Esporta());

        Assert.Equal("SELECT Totale FROM Ordini",
            Esegui(ricaricata, "SELECT col_dec18_2_001 FROM tbl_001"));
    }

    [Fact]
    public void De_anonimizzare_due_volte_non_cambia_il_risultato()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        mappa.Ottieni(Ruolo.Colonna, "Totale", "int");

        var una = de.DeAnonimizza("SELECT col_int_001 FROM tbl_001").Testo;
        var due = de.DeAnonimizza(una).Testo;

        Assert.Equal(una, due);
    }

    [Fact]
    public void Un_nome_originale_che_somiglia_a_un_anonimo_non_viene_riscritto()
    {
        // Il motore evita già di generare nomi uguali a quelli reali; qui verifichiamo solo che una passata sola
        // non sostituisca a catena (l'originale "tbl_002" non diventa a sua volta qualcos'altro).
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "tbl_002");
        mappa.Ottieni(Ruolo.Tabella, "Clienti");

        var r = de.DeAnonimizza("tbl_001 tbl_003");

        Assert.Equal(2, r.Sostituzioni);
        Assert.Equal("tbl_002 Clienti", r.Testo);
    }

    [Fact]
    public void Argomenti_nulli_sono_rifiutati()
    {
        Assert.Throws<ArgumentNullException>(() => new DeAnonimizzatore(null!));
        var (_, de) = Crea();
        Assert.Throws<ArgumentNullException>(() => de.DeAnonimizza(null!));
    }

    [Fact]
    public void Testo_lungo_con_molti_nomi()
    {
        var (mappa, de) = Crea();
        mappa.Ottieni(Ruolo.Tabella, "Ordini");
        var testo = string.Join("\n", Enumerable.Repeat("SELECT * FROM tbl_001 WHERE a = 1;", 5000));

        var r = de.DeAnonimizza(testo);

        Assert.Equal(5000, r.Sostituzioni);
        Assert.DoesNotContain("tbl_001", r.Testo);
    }
}
