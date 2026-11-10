# Anonimizzatore T-SQL — istruzioni per Claude

App desktop Windows, locale e offline, che anonimizza script T-SQL (SQL Server) da passare all'IA e de-anonimizza le risposte.
Proprietario: Mattia (repo pubblica `mattiafili94/Anonimizzatore`, licenza MIT).
Lingua di lavoro: **italiano** (testi dell'app, commenti, messaggi di commit). Mattia è un DBA SQL Server, ma è alle
prime armi con Claude Code: spiegare le cose in modo semplice.

## Stato attuale (10/11/2026)

- Brainstorming concluso: le decisioni sono qui sotto.
- Repo GitHub: https://github.com/mattiafili94/Anonimizzatore. La cartella locale
  `C:\Users\matti\Progetti_Claude\Anonimizzatore` (accanto ad Arco) è collegata a git sul ramo `main`.
- Piano della fase 1 **approvato** il 10/11/2026: [docs/piano-fase-1.md](docs/piano-fase-1.md) (decisioni nella
  sezione 7).
- Si sviluppa in locale sul PC Windows di Mattia (.NET SDK 10.0.403). Test: `dotnet test` dalla cartella principale.
- Il PC non ha sorgenti NuGet globali: le indica il `nuget.config` della repo (nuget.org). Python non è installato.
- **Tappa 1 fatta** (10/11/2026): soluzione `Anonimizzatore.slnx`, `src/Anonimizzatore.Motore` (ScriptDom 180.140.0,
  `LettoreScript`), `tests/Anonimizzatore.Motore.Test` (xUnit, esempi inventati in `Esempi/`), 4 test verdi.
- **Tappa 2 fatta** (10/11/2026): `Mappa/` (`Ruolo`, `CodiceTipo`, `GeneratoreNomi`, `MappaCliente`) e `Profili/`
  (`Profilo`, `ArchivioProfili`, `ProtezioneDpapi`), 63 test verdi. Scelte di dettaglio:
  - ruoli della stessa "famiglia" condividono contatore e nomi: server + linked server (`srv_`), login + utente
    (`usr_`), variabile + parametro;
  - un solo file cifrato per profilo, `{Id}.anonprofile` (profilo + mappa insieme): il nome del cliente non compare
    nel nome del file;
  - i nomi generati saltano i nomi reali già in mappa e i "nomi riservati" (es. dallo schema importato).
- **Tappa 3 fatta** (10/11/2026): `Classificazione/` (`ListeBase`, `Conoscenza`, `PreAnalisi`, `Classificatore`) e
  `Anonimizzazione/AnonimizzatoreScript` (punto d'ingresso: `Anonimizza(script, profilo, mappa)`), 99 test verdi.
  Livello 1 completo: server, database, schema, tabelle, viste, procedure, funzioni, trigger, temporanee, nomi in più
  parti, parentesi, prefissi di colonna (`Ordini.Id`), alias/CTE in chiaro. Scelte di dettaglio:
  - la classe si chiama `AnonimizzatoreScript` (non `Anonimizzatore`: sarebbe uguale al namespace e darebbe ambiguità);
  - script con errori di sintassi: `Testo = null` + avviso, mai testo non anonimizzato (la modalità di riserva è la
    tappa 10);
  - tipi definiti dall'utente (`dbo.CodiceCliente`) restano in chiaro con avviso;
  - i nomi veri presenti negli script sono "riservati" nella mappa: un nome generato non li copia mai;
  - limite noto: la mappa tiene la **prima scrittura** di un nome (`ORDINI` vs `Ordini`): l'andata e ritorno è
    identica solo se il maiuscolo/minuscolo è coerente, altrimenti coincide a meno delle maiuscole.
  - non ancora coperti (tappe successive): colonne/variabili/constraint (4), liste (5), commenti (6), sicurezza (7),
    stringhe e SQL dinamico (8-9). Per ora, per esempio, `PK_Ordini` e il testo dei commenti restano in chiaro.
- **Pezzi sviluppati in parallelo e riuniti in `main`** (10/11/2026, 277 test verdi in tutto). Sono classi autonome,
  **non ancora collegate** a `AnonimizzatoreScript` (si agganciano nelle tappe indicate):
  - `Commenti/GestoreCommenti` (tappa 6): commenti ↔ segnaposto `/* c001 */`, con elenco dei segnaposto persi;
  - `Deanonimizzazione/DeAnonimizzatore` (tappa 12): rimette i nomi nel testo libero dell'IA, con report;
  - `Infrastruttura/AnalizzatoreInfrastruttura` (parte testo della tappa 8): percorsi, IP, host, email, URL, stringhe
    di connessione, con metodo inverso `Ripristina`. Ha aggiunto i ruoli `Cartella`, `File`, `Ip`, `Email` in
    `Ruolo` (famiglie `dir`, `file`, `ip`, `mail`).
  Da fare quando si agganciano: il `DeAnonimizzatore` dovrà ripristinare anche i segnaposto commento e i valori
  dell'infrastruttura; i test di andata e ritorno useranno quello vero al posto di `Aiuti.RipristinaPerProva`.
- **Tappa 4 fatta** (10/11/2026): colonne, variabili, parametri, alias, indici e constraint, 331 test verdi. Novità:
  - tipi ricavati dallo script (`Conoscenza.DichiaraTipo`): CREATE/ALTER TABLE, parametri, DECLARE (dichiarati) e
    CAST/CONVERT (dedotti, valgono solo se manca il dichiarato) → `col_vc20_001`, `@p_vc20_001`, altrimenti `col_x_`;
  - nuovo ruolo `Alias` (famiglia `al_`) per alias di tabella e nomi di CTE; gli alias di **colonna** (`AS Totale`) sono
    `Colonna`, perché poi si usano come colonne (`ORDER BY Totale`);
  - `Blacklist` (per parole: `Acme` coincide con `AcmeOrdini`, `@CodiceAcme`, `acme_srl`; non con `Acmeria`) e
    `Parole.Dividi` (underscore/CamelCase/cifre). La blacklist **non** tocca gli oggetti di sistema;
  - i parametri con nome di procedure di sistema (`@job_name`) e `@@variabili` non si toccano mai;
  - nomi composti di indici/constraint (fase B, dopo che tutti gli oggetti hanno il nome anonimo): si sostituiscono le
    parti pulite (`FK_Righe_Ordini` → `FK_tbl_002_tbl_001`); se una parte è attaccata ad altro (`IXOrdini`,
    `IX_Ordini2`) o c'è una parola in blacklist o la matrice lo chiede → nome intero (`ix_001`, `pk_001`...);
  - `AnonimizzatoreScript` lavora a fasi: A = oggetti/colonne/variabili/alias, B = indici e constraint;
  - le prove di andata e ritorno usano il vero `DeAnonimizzatore` (aggiunto il prefisso `al_`).
  - Scelta: una colonna che ha per caso lo stesso nome di una tabella (`... AS Righe`) resta in chiaro, come tutte le
    colonne.
- **Tappa 5 fatta** (10/11/2026): whitelist, 356 test verdi. `Whitelist` (per nome intero e per parole) e lista base
  `ListeBase.ParoleGeneriche` (sigle tecniche, verbi, preposizioni, nomi generici IT/EN: `Data`, `Id`, `Codice`,
  `Log`, `Storico`, `Get`...; **niente parole di dominio**: ordini, clienti, fatture...). Regole di decisione, in
  quest'ordine (`AnonimizzatoreScript.Liste`): sistema → in chiaro; **blacklist → anonimizzato (vince su tutto)**;
  matrice in chiaro → in chiaro; whitelist → in chiaro; altrimenti anonimizzato. Scelte di dettaglio:
  - un nome resta in chiaro se **tutte** le sue parole sono generiche (`usp_GetData`); basta una parola di dominio
    (`usp_GetOrdini`) per anonimizzarlo; le cifre contano come generiche;
  - una voce del cliente a **una parola** vale come nome intero e come parola generica; una voce di **più parole**
    (`OrdiniAperti`) vale solo come nome intero: le sue parole non diventano generiche;
  - un nome che ha già un nome anonimo in mappa **resta anonimizzato** anche se poi entra in whitelist (nomi stabili);
    si cambia solo con "rigenera nomi" (`MappaCliente.Contiene(ruolo, nome)`);
  - vale per tutti i ruoli, anche con la matrice che anonimizza colonne/indici: `Id`, `Nome` restano in chiaro.
  - Ancora da fare per la blacklist: applicarla alle **stringhe** e ai commenti (tappe 6 e 9).
- **Prossimo passo**: tappa 6 (agganciare `GestoreCommenti` all'`AnonimizzatoreScript`: segnaposto `/* c001 */`, testi
  originali nella mappa del cliente, ripristino nel `DeAnonimizzatore`). Poi 7-13 (sicurezza, infrastruttura nelle
  stringhe e SQL dinamico, SQLCMD/modalità di riserva, legenda, de-anonimizzazione completa, anti-fuga).

## Regole fisse (repo pubblica)

- **Mai** script reali dei clienti nella repo, neanche nei test: solo esempi inventati.
- Gli script reali per le prove di Mattia vanno nella cartella `prove-locali/`, esclusa da git.
- Profili, mappe e dati locali non entrano mai nella repo (`.gitignore`).
- Nessun nome di cliente nel codice o nelle liste base: la blacklist vive solo nei profili locali.
- L'app non fa **nessuna chiamata di rete**: niente CDN, niente telemetria, tutte le risorse incluse.

## Obiettivo

App desktop **locale** (Windows) che riceve script T-SQL per SQL Server (query, stored procedure, DDL, script di
sicurezza, backup/restore, job), li restituisce anonimizzati da passare all'IA e poi **de-anonimizza** la risposta
dell'IA. Priorità: un risultato che l'IA capisca il meglio possibile. L'app è **distribuita ai colleghi** e funziona
**completamente offline**.

## Decisioni prese (brainstorming del 9/11/2026)

- **Stack**: C# su **.NET 10** (LTS), parser **Microsoft ScriptDom** (`Microsoft.SqlServer.TransactSql.ScriptDom`).
- **Interfaccia**: **WPF + Blazor Hybrid** (BlazorWebView su WebView2), editor **Monaco** con diff affiancato.
- **Design**: design system **UI Top** (lo stesso dell'app Arco; i token sono in `../Arco/design-system/`,
  artifact: https://claude.ai/artifact/BEUsaG1jRRShkG7r6DdHXN).
- **Architettura**: motore di anonimizzazione in una libreria separata senza dipendenze dall'interfaccia, con progetto
  di test dedicato; l'interfaccia è uno strato sottile sopra.
- **De-anonimizzazione**: sì, con mappa persistente **per cliente**, cifrata con DPAPI.
- **Profili per cliente**: **personali**: ogni collega usa l'app in modo indipendente e crea i propri profili.
  Nessuna condivisione tra colleghi.
- **Schema del DB facoltativo**: l'app deve funzionare bene anche senza schema importato.
- **Input/output**: tutto via **copia-incolla** (niente file `.sql` in uscita). Uno o più script alla volta.
- **Livelli v1**: 1 nomi oggetti, 2 sicurezza (login/utenti/ruoli/AD), 3 infrastruttura (percorsi, IP, host),
  5 commenti. Il livello 4 (letterali/dati) resta fuori dalla v1.
- **Nomi parlanti**: prefisso del ruolo + tipo + numero.
- **Colonne in chiaro di default**, con eccezioni via blacklist.
- **Oggetti non classificabili senza schema**: prefisso `tbl_`.
- **Whitelist**: sia per nome intero sia **per parole**.
- **Commenti**: sostituiti da **segnaposto ripristinabile** (`/* c001 */`).
- **Legenda** e **schema anonimizzato allegato**: sì, entrambi.
- **Collation**: confronto dei nomi case-insensitive.
- **Script di test**: Mattia fornisce esempi realistici quando si arriva alla fase di test.

## Distribuzione

- **Cartella portabile (zip)** self-contained: runtime .NET incluso, si estrae e si avvia. Mattia la invia ai colleghi.
- Dati dell'utente (profili, mappe, impostazioni) in `%APPDATA%`, fuori dalla cartella: **aggiornare = sostituire la
  cartella**, i profili restano.
- Nessun aggiornamento automatico. Numero di versione visibile nell'app.
- **Nessuna firma del codice**: al primo avvio SmartScreen può mostrare "Windows ha protetto il PC" →
  "Ulteriori informazioni" → "Esegui comunque". Uno zip ricevuto via mail può richiedere "Sblocca" nelle proprietà.
- **PC colleghi**: Windows 10/11 con diritti di amministratore. All'avvio l'app verifica WebView2 e, se manca, mostra
  un messaggio chiaro con le istruzioni.
- Possibile aggiunta futura: backup dei profili esportabile con password (con un PC nuovo DPAPI non li recupera).

## Interfaccia: perché WPF + Blazor Hybrid

- Monaco offre già evidenziazione T-SQL e **diff editor affiancato**, la parte più complessa dell'interfaccia.
- Interfaccia in HTML/CSS: si applica direttamente UI Top ed è più rapida da iterare.
- Logica tutta in C# nello stesso processo, offline.
- Costi: un po' di interop JavaScript per Monaco, app leggermente più pesante. Accettabili.
- Scartato: WPF nativo (AvalonEdit + DiffPlex), con diff e stile da costruire a mano in XAML.
- Rischio basso: il motore è separato, l'interfaccia si può sostituire senza toccarlo.

## Regole per tipo di oggetto (default confermati)

Matrice configurabile per cliente: per ogni tipo di oggetto "anonimizza" oppure "in chiaro". Default:
- **Anonimizzati**: server, linked server, database, schema (tranne `dbo`), tabelle, viste, SP, funzioni, trigger, job,
  login, utenti, ruoli, domini AD, percorsi, IP, host.
- **In chiaro**: colonne, variabili/parametri, alias, indici e constraint il cui nome contiene solo parti in chiaro.
- **Eccezioni**: un nome in chiaro che contiene una parola in blacklist viene anonimizzato comunque
  (`col_vc50_012`, `@v_int_004`).
- Nomi composti che includono oggetti anonimizzati (`FK_Clienti_Ordini`, `IX_Ordini_Data`): le parti corrispondenti a
  oggetti anonimizzati vengono sostituite.

## Flusso d'uso

1. Scegli il profilo cliente.
2. Incolli uno o più script (ognuno nel suo riquadro).
3. L'app li anonimizza con la stessa mappa e mostra il confronto affiancato con gli avvisi.
4. **Copia**: un solo testo negli appunti con legenda, eventuale schema anonimizzato e script separati da intestazioni
   generate dall'app (`-- Script 1`, `-- Script 2`…).
5. Incolli la **risposta intera dell'IA** (testo + codice): la de-anonimizzazione lavora sul testo libero, non solo sul
   SQL. Risultato copiato negli appunti.
- Idea per dopo: tasti rapidi globali (anonimizza/de-anonimizza direttamente gli appunti).

## Aiutare l'IA a capire

- **Legenda** in testa al testo copiato: spiega la convenzione dei nomi (`tbl_` = tabella, `col_vc50_` = varchar(50),
  `col_x_` = tipo ignoto…).
- **Schema anonimizzato allegato** (solo con schema importato): DDL anonimizzato delle tabelle citate, con colonne e
  tipi, PK, FK e indici.

## Senza schema

- Tipi ricavati da DDL, DECLARE, parametri, CAST/CONVERT presenti negli script; altrimenti `col_x_` (solo per le
  colonne anonimizzate).
- Oggetti nel FROM non classificabili: `tbl_`.
- La mappa del cliente cresce nel tempo; i nomi già assegnati restano **stabili**. Si aggiornano solo con un comando
  esplicito ("rigenera nomi"), da usare all'inizio di una nuova conversazione con l'IA.

## Formato nomi

- Oggetti: `srv_01`, `db_01`, `sch_01` (`dbo` in chiaro), `tbl_001`, `vw_001`, `usp_001`, `fn_001`, `trg_001`,
  `ix_001`, `pk_/fk_/df_/ck_001`, `job_001`, `usr_001`, `rol_001`, `dom_01`
- Colonne (solo se anonimizzate): `col_vc50_012`, `col_nvcmax_004`, `col_int_003`, `col_dec18_2_007`, `col_dt_018`,
  `col_bit_002`; tipo ignoto `col_x_019`
- Variabili/parametri (solo se anonimizzati): `@v_int_004`, `@p_vc20_002`; temporanee `#tmp_003`
- Fonte dei tipi: script + **import dello schema** facoltativo (query su sys.objects/sys.columns/PK/FK fornita dall'app).

## Whitelist e blacklist

- Lista base precompilata + lista specifica per cliente.
- Whitelist per nome intero e per parole (nome spezzato su CamelCase/underscore; se tutte le parole sono generiche,
  resta in chiaro).
- La blacklist vince sempre sulla whitelist e si applica anche a colonne, variabili, stringhe.

## Commenti

- `--` e `/* */` sostituiti da segnaposto `/* c001 */`, anche dentro il SQL dinamico analizzato.
- In de-anonimizzazione il testo originale torna al suo posto se l'IA ha conservato il segnaposto; quelli persi vengono
  segnalati nel report.

## Gestione insidie

- **Principio**: albero sintattico per *classificare*, sostituzione sui *token* del testo originale (formattazione
  intatta).
- **SQL dinamico**: stringa completa → analizzata come T-SQL in modo ricorsivo (apici raddoppiati); frammenti
  concatenati → sostituzione per dizionario + avviso. Vale anche per `@command` di `sp_add_jobstep`.
- **Parentesi quadre / nomi in più parti**: chiave senza parentesi e case-insensitive; parentesi conservate; ogni parte
  mappata per ruolo.
- **Colonne anonimizzate**: mappa globale per nome a livello di cliente (non per tabella).
- **Alias**: in chiaro, salvo parole in blacklist.
- **Oggetti di sistema**: lista fissa (built-in, `sp_*`/`xp_*` di sistema, DB di sistema, `sys`, `INFORMATION_SCHEMA`,
  DMV).
- **GO / SQLCMD**: batch gestiti da ScriptDom; righe `:setvar`/`:connect` trattate a parte.
- **Script con errori**: modalità di riserva solo sui token, con avviso.
- **Sicurezza**: `[DOMINIO\utente]` → `[dom_01\usr_007]`; password e SID sostituiti da un segnaposto e **non salvati**
  nella mappa.
- **Percorsi/infrastruttura nelle stringhe**: UNC, percorsi locali, IP, host, email, URL; struttura ed estensione
  mantenute.
- **Collisioni**: controllo dei nomi generati contro parole chiave e nomi reali.
- **De-anonimizzazione**: formato riconoscibile anche dentro nomi composti inventati dall'IA, case-insensitive, con e
  senza parentesi; report dei token senza corrispondenza.
- **Controllo anti-fuga**: ricerca nell'output dei nomi originali anonimizzati e della blacklist + nuovo parsing di
  validità; copia bloccata finché l'utente non conferma.
- Senza livello 4 i valori (CF, nomi, importi) restano in chiaro → possibile avviso leggero su email/CF/IBAN.

## Fasi di sviluppo (confermate)

1. **Motore + test**: parser, classificazione, mappa, anonimizzazione, de-anonimizzazione, controllo anti-fuga. Senza
   interfaccia, verificato con script di esempio.
2. **Interfaccia**: profili cliente, incolla/confronto/copia, de-anonimizzazione, impostazioni (matrice, whitelist,
   blacklist), import dello schema. Design UI Top.
3. **Pacchetto**: cartella portabile self-contained, controllo WebView2, numero di versione, istruzioni per i colleghi.
