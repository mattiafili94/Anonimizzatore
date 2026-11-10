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
  Agganciati tutti e tre: `DeAnonimizzatore` (tappa 4), `GestoreCommenti` (tappa 6), `AnalizzatoreInfrastruttura` (tappa 8).
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
- **Tappa 6 fatta** (10/11/2026): commenti (livello 5), 368 test verdi. `GestoreCommenti` agganciato:
  - `MappaCliente.Commenti` tiene i testi originali (finiscono nel file cifrato del profilo: `DatiMappa.Commenti`,
    opzionale, i profili vecchi si caricano lo stesso); `Rigenera()` li azzera;
  - `AnonimizzatoreScript` sostituisce i commenti come **ultimo passo** di ogni script; la numerazione prosegue tra
    script diversi e tra una chiamata e l'altra;
  - **lo stesso testo di commento riceve sempre lo stesso segnaposto** (modifica mia a `GestoreCommenti`): rianonimizzare
    lo stesso script non cambia i numeri e la mappa non cresce con i duplicati;
  - `DeAnonimizzatore` ripristina i commenti **dopo** i nomi (un commento che contiene `tbl_001` non viene riscritto) e
    li riporta in `RisultatoDeAnonimizzazione.Commenti` (`Persi`, `Sconosciuti`, `Spostati`);
  - un commento su più righe diventa un segnaposto su una riga: i numeri di riga dello script anonimizzato non
    coincidono più con l'originale (limite noto, da tenere presente se l'IA cita "riga 42");
  - script con errori di sintassi: nessun numero di commento consumato.
  - Non ancora fatto: commenti dentro il SQL dinamico (tappa 9).
- **Tappa 7 fatta** (10/11/2026): sicurezza (livello 2), 402 test verdi. Novità:
  - login, utenti, ruoli (`CREATE/ALTER/DROP LOGIN|USER|ROLE`, ruoli di server e applicativi, `ALTER ROLE ... ADD
    MEMBER`, `WITH NAME/DEFAULT_SCHEMA/DEFAULT_DATABASE`), `GRANT/DENY/REVOKE` (principali, `AS`, oggetto, schema,
    database, colonne), `ALTER AUTHORIZATION`, `CREATE SCHEMA ... AUTHORIZATION`;
  - `DOMINIO\utente` si spezza: `[ACMEDOM\mario]` → `[dom_01\usr_001]` (il dominio segue la matrice `Dominio`, la blacklist
    vince; l'utente è `usr_` come i login/utenti con lo stesso nome). `utente@dominio.com` → un solo `usr_`;
  - restano in chiaro login/utenti/ruoli di sistema (`sa`, `public`, `dbo`, `guest`, `db_*`, ruoli fissi di server,
    `##MS_...##`) e gli account `NT AUTHORITY\`, `NT SERVICE\`, `BUILTIN\` (`ListeBase.PrincipaleDiSistema`);
  - nome che può essere utente o ruolo (`GRANT ... TO nome`): è `rol_` se è un ruolo dichiarato negli script o già nella
    mappa (`Conoscenza.RuoloPrincipale`), altrimenti `usr_`. Un `GRANT` su un nome mai visto che dà `EXECUTE` è una
    procedura (`usp_`), non una tabella;
  - **password, hash di password e SID vengono sempre cancellati** e non passano mai dalla mappa (nemmeno se il profilo
    lascia tutto in chiaro): `'<password>'` (o `N'<password>'`), `0x00` per un hash, `0x0000…` (16 byte) per un SID.
    Coperti: `CREATE/ALTER LOGIN`, `CREATE USER ... WITH PASSWORD`, ruoli applicativi, chiave master, certificati, chiavi
    simmetriche, `CREATE CREDENTIAL ... SECRET`, le `sp_` di sicurezza (`sp_addlogin`, `sp_password`,
    `sp_change_users_login`, `sp_addlinkedsrvlogin`...). I segreti **non tornano** nel ripristino: l'IA vede il segnaposto;
  - nomi dentro stringhe: `EXECUTE AS USER|LOGIN = '...'`, `WITH EXECUTE AS '...'`, `SETUSER`, `IDENTITY` delle
    credenziali e gli argomenti delle `sp_` di sicurezza (`sp_addrolemember`, `sp_adduser`, ...: tabella in
    `ProcedureSicurezza`), anche con parametri con nome. Il prefisso `N` e gli apici raddoppiati si conservano
    (`NomeTrovato.DaStringa`);
  - **`LettoreScript` non solleva più eccezioni**: il parser di Microsoft va in errore interno (NullReference) su certe
    combinazioni (per esempio `CREATE SCHEMA ... AUTHORIZATION` seguito da altri statement senza `GO`); ora lo script
    risulta "con errori" e non viene restituito.
  - Non ancora coperti: nomi di certificati/chiavi/credenziali, proprietario di certificati, job di SQL Agent
    (`sp_add_job @owner_login_name`), `datasrc`/`provstr` di `sp_addlinkedserver` (tappa 9).
- **Tappa 8 fatta** (10/11/2026): infrastruttura (livello 3), 424 test verdi. Novità:
  - **fase C** di `AnonimizzatoreScript`: ogni letterale stringa non già trattato come nome/segreto passa da
    `AnalizzatoreInfrastruttura` (percorsi UNC/locali, IP, host, email, URL, stringhe di connessione). Dopo le fasi A e B,
    così `Vendite.bak` diventa `db_01.bak` se `Vendite` è già `db_01`; altrimenti `file_NNN`. Avvisi con il numero di riga;
  - nomi di **database** in `BACKUP/RESTORE DATABASE`, `ALTER DATABASE` (anche `MODIFY NAME`), `DROP DATABASE`,
    `DBCC CHECKDB/CHECKALLOC/CHECKCATALOG/CHECKFILEGROUP/SHRINKDATABASE/UPDATEUSAGE` (con o senza apici): prima restavano
    in chiaro. Nomi **logici di file** (`RESTORE ... MOVE`, `CREATE DATABASE ... NAME =`, `DBCC SHRINKFILE`) e dispositivi di
    backup → `Ruolo.File` (`file_001`);
  - `sp_addlinkedserver`: `@datasrc`/`@location` sono un server (`AnonimizzaServer`: stesso risultato di una stringa di
    connessione, `SRVSQL02\ISTANZA1` → `srv_02\srv_03` ovunque), `@catalog` un database, `@provstr` va al passo stringhe;
  - **credenziali nelle stringhe di connessione** (modifica a `AnalizzatoreInfrastruttura`, prima le lasciava e avvisava):
    `Password=`/`Pwd=` → `<password>` (mai in mappa), `User ID=`/`UID=` → `usr_NNN` (`DOM\x` → `dom_NN\usr_NN`; `sa` e
    account di sistema restano);
  - **ripristino per contesto** (`RipristinaNeiContesti`, usato dal `DeAnonimizzatore` per primo, poi nomi, poi commenti):
    `dir_`, `file_`, `ip_`, `mail_` si ripristinano ovunque; `srv_`, `db_`, `dom_` solo dentro percorsi, URL, stringhe di
    connessione e host (`srv_01.dom_01.local`), altrimenti nel codice ci pensa il ripristino dei nomi, che aggiunge le
    parentesi quadre (`[SRV-REMOTO].[Vendite-Nuove]`). Il riconoscimento degli host anonimi accetta dopo il primo pezzo solo
    `dom_NN` o un suffisso noto (`local`, `com`...), così non scambia `srv_01.db_01.dbo` per un host;
  - non più eseguiti (non provabili con il parser 170): le opzioni `PASSWORD`/`MEDIAPASSWORD` di `BACKUP`/`RESTORE`. Uno
    script che le usa dà errore di sintassi e non viene restituito.
  - **Limiti noti**: i testi liberi nelle stringhe (`BACKUP ... NAME = N'Vendite full'`, il percorso di un URL
    `/report/vendite`, messaggi di `PRINT/RAISERROR`) restano in chiaro (livello 4, v1 fuori; li segnalerà l'anti-fuga della
    tappa 13); le voci della matrice per `Cartella`/`File`/`Ip`/`Email` non sono ancora rispettate (l'analizzatore
    anonimizza sempre); la blacklist non entra ancora nelle stringhe.
- **Tappa 9 fatta** (10/11/2026): SQL dinamico e job di SQL Agent, 445 test verdi. `AnonimizzatoreScript` ora lavora su
  **unità** (`Unita`): uno script, oppure una stringa che contiene T-SQL (`SqlDinamico.TrovaFigli`). Novità:
  - **ricorsione**: una stringa la cui prima parola è un inizio di istruzione e che ScriptDom legge senza errori (con gli apici
    raddoppiati tolti) è una unità figlia, trattata come uno script a sé con la stessa mappa (nomi, tipi, commenti,
    stringhe); fino a 8 livelli. Alla fine torna nella stringa della madre con gli apici raddoppiati. Funziona per
    `EXEC('...')`, `sp_executesql`, stringhe in variabili, `@command` di `sp_add_jobstep`;
  - `sp_executesql`: la dichiarazione dei parametri (`N'@c varchar(20), @n int OUTPUT'`) è una unità (letta dentro un
    `CREATE PROCEDURE sys.__anonimizzatore__ ... AS SELECT 1` aggiunto e poi tolto), e gli argomenti (`@c = ...`) sono
    variabili del cliente: stessi nomi nella stringa, nella dichiarazione e negli argomenti (solo `@stmt`/`@params` sono di
    sistema);
  - una stringa riconosciuta come nome o segreto (`EXECUTE AS USER = 'Select Mario'`) non è SQL: l'unità figlia è scartata;
  - **SQL a pezzi o con errori** (la stringa inizia come SQL ma non è valida: `' FROM '`, `N'SELECT * FROM ' + @t`):
    sostituzione per dizionario dei nomi già in mappa (`SqlDinamico.Dizionario`: parole intere, anche tra `[ ]`, mai una
    variabile al posto di un oggetto) + avviso con la riga. Una stringa che è **solo un nome di oggetto**
    (`OBJECT_ID(N'dbo.Ordini')`, `@tabella = N'Ordini'`, `COL_LENGTH('dbo.Ordini','Col')`) si sostituisce per dizionario
    senza avviso;
  - **job**: `sp_add_job`, `sp_update_job`, `sp_delete_job`, `sp_start_job`, `sp_stop_job`, `sp_add_jobstep`,
    `sp_update_jobstep`, `sp_add_jobserver`... (tabella in `ProcedureSicurezza`): `@job_name` → `Ruolo.Job` (`job_001`),
    `@owner_login_name`, `@database_name`, `@server`/`@server_name`. `(local)`, `.`, `localhost` restano. `@step_name` e le
    descrizioni sono testo libero (non sostituiti);
  - **righe di comando** (`xp_cmdshell`, passo di job non TSQL): lette come testo libero (`RisultatoClassificazione.Comandi`),
    così `/E` non diventa una cartella;
  - nel ripristino un nome intero dentro una stringa (`N'job_001'`) non prende le parentesi quadre;
  - `sysname` non dà più l'avviso "tipo definito dall'utente".
  - **Limiti noti**: un commento `--` che chiude una stringa (`N'SELECT 1 -- nota'`) torna con un a capo prima dell'apice
    (il ripristino non può sapere che dopo c'è la fine della stringa); i pezzi di SQL che non iniziano con una parola
    chiave (`'.dbo.'`, `'[dbo].['`) e i nomi che arrivano da variabili non si riconoscono (avviso); i numeri dei
    segnaposto dei commenti seguono prima il SQL dinamico poi gli script.
- **Tappa 10 fatta** (10/11/2026): SQLCMD e modalità di riserva, 494 test verdi. Novità:
  - **SQLCMD** (`Sqlcmd.Dividi`, `AnonimizzatoreScript.Sqlcmd.cs`): le righe `:setvar`, `:connect`, `:r`, `:out`, `:error`, `:ed`,
    `:on`, `:quit`, `:exit`, `!!` e `GO n` si separano dal T-SQL (non dentro un commento `/* */`); ogni blocco di T-SQL tra
    due righe SQLCMD è una unità a sé (un blocco rotto non rovina gli altri; le righe degli avvisi si riportano allo script).
    Le righe SQLCMD si elaborano **dopo** i blocchi di T-SQL (così i nomi già noti si riconoscono):
    - `:setvar NOME valore`: il **nome della variabile** dice cosa contiene: `pass|pwd|secret` → `<password>`;
      `path|dir|folder|cartella|percorso|file` → percorso; `database|dbname|db|catalog` → `Database`;
      `server|srv|instance|istanza|host|node` → `Server`; `schema`; `user|login|utente|account|owner` → `Login`
      (`DOM\x` si spezza); `job`; altrimenti percorsi/IP/email e poi nomi già noti. Sistema, matrice, blacklist e whitelist
      valgono come per gli script. Un valore con `$(...)` non si tocca;
    - `:connect server -U utente -P password`, `:r|:out|:error|:ed file` (percorso, oppure solo nome di file → `file_NNN`
      con la sua estensione), `!!comando`;
    - **opzioni di `sqlcmd`/`osql`/`bcp`** (`-S` server, `-U` utente, `-P` password cancellata, `-d` database, `-Q` query, `-i/-o`
      file) anche dentro `xp_cmdshell`, passi di job CmdExec e `:connect`/`!!`;
    - le variabili `$(Db)` nei nomi (`[$(Db)].dbo.Ordini`, anche senza parentesi) non si toccano: il valore sta nel `:setvar`;
  - **modalità di riserva** (`ModalitaRiserva`): un blocco con errori di sintassi non si scarta più, si leggono i token. Si
    sostituiscono: il nome dopo `FROM/JOIN/INTO/UPDATE/TABLE/MERGE/REFERENCES` (tabella), `VIEW`, `PROCEDURE/PROC/EXEC/EXECUTE`,
    `FUNCTION`, `TRIGGER`, `SCHEMA`, `DATABASE/USE` (con schema, database e server davanti), i nomi già in mappa (da altri
    blocchi o da sessioni precedenti), le parole della blacklist (colonne/variabili) e `PASSWORD = '...'` / `SID = 0x...`.
    Il resto (colonne mai viste, alias) resta in chiaro. Stesse regole di decisione, commenti e stringhe degli script validi.
    Avviso sempre presente, con la riga e il messaggio dell'errore. Classificati **dopo** i blocchi validi (serve la mappa);
  - uno script resta `null` solo se non si legge nemmeno un token. `LettoreScript` garantisce sempre la lista dei token
    (prima, con un errore del lessico come in `:r D:\x.sql`, restava `null`).
  - **Limiti noti**: in modalità di riserva un'espressione come `Ordini.Id` si riconosce solo se `Ordini` è già in mappa; le
    righe SQLCMD dentro una stringa su più righe o un commento non `/* */` (es. `--`) sono lette come SQLCMD solo se
    iniziano la riga; `:setvar` con nome non riconoscibile e valore sconosciuto resta in chiaro (si vede dal dizionario solo
    dopo che il nome è comparso altrove).
- **Prossimo passo**: tappa 11 (legenda e testo unico da copiare: convenzione dei nomi per l'IA, schema anonimizzato allegato se
  c'è lo schema importato, script separati da `-- Script 1`, `-- Script 2`...). Poi 12 (de-anonimizzazione: report completo) e
  13 (controllo anti-fuga: nomi originali e blacklist nell'output + nuova lettura di validità; testi liberi, descrizioni dei
  job, `NAME` dei backup).

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
- **GO / SQLCMD**: `GO` semplice lo gestisce ScriptDom; `GO 5` (con conteggio) e le righe `:setvar`/`:connect`/`:r`/`!!` sono
  errori di sintassi per ScriptDom e si trattano a parte (vedi tappa 10).
- **Script con errori**: modalità di riserva solo sui token, con avviso (fatto, tappa 10).
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
