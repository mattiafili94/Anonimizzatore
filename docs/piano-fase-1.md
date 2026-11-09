# Piano della fase 1 — Motore + test

Stato: **bozza, da approvare** (9/11/2026).
Obiettivo: una libreria C# che anonimizza e de-anonimizza script T-SQL, senza interfaccia, verificata da test
automatici su script di esempio inventati. Quando la fase 1 è finita, l'interfaccia (fase 2) deve solo "chiamare" il
motore.

---

## 1. Struttura della soluzione

```
Anonimizzatore.sln
src/
  Anonimizzatore.Motore/            libreria .NET 10, nessuna dipendenza dall'interfaccia
tests/
  Anonimizzatore.Motore.Test/       progetto di test (xUnit)
    Esempi/                         script T-SQL INVENTATI (mai script reali)
docs/
  piano-fase-1.md                   questo file
```

- Pacchetti NuGet: `Microsoft.SqlServer.TransactSql.ScriptDom` (ultima versione, oggi 180.x), `xunit`.
- La cifratura DPAPI funziona solo su Windows: il motore la usa tramite un'interfaccia (`IProtezioneDati`), così i
  test girano ovunque con una versione finta, e su Windows si usa quella vera (`ProtectedData`).
- Il motore **non legge né scrive file da solo** tranne che tramite la classe dello "archivio profili", che riceve il
  percorso dall'esterno (in fase 2 sarà `%APPDATA%\Anonimizzatore`).

## 2. Come funziona il motore (in breve)

Per ogni script il motore fa quattro passi:

1. **Lettura** — ScriptDom trasforma il testo in un elenco di *token* (parole, simboli, stringhe, commenti) e in un
   *albero sintattico* che dice cosa rappresenta ogni pezzo.
2. **Classificazione** — percorrendo l'albero, a ogni nome si assegna un **ruolo**: server, database, schema, tabella,
   vista, SP, funzione, trigger, colonna, variabile, alias, login, utente, ruolo, job, indice, constraint, ecc.
   Gli oggetti nel `FROM` che non si possono classificare diventano tabelle (`tbl_`).
3. **Decisione** — per ogni nome si decide "anonimizza" o "in chiaro", nell'ordine:
   oggetti di sistema → sempre in chiaro; blacklist → sempre anonimizzato; matrice per tipo del profilo;
   whitelist per nome intero e per parole.
4. **Sostituzione** — i token da anonimizzare vengono sostituiti **sul testo originale** (spazi, a capo, maiuscole e
   parentesi quadre restano come erano). Il nome anonimo viene dalla mappa del cliente: se il nome è già noto si
   riusa quello, altrimenti se ne crea uno nuovo.

La de-anonimizzazione fa il contrario sul **testo libero** della risposta dell'IA: cerca i nomi nel formato del motore
(`tbl_001`, `col_vc50_012`, `/* c001 */`…) anche dentro nomi composti inventati dall'IA, e li rimette a posto.

## 3. Componenti (classi principali)

| Componente | Cosa fa |
|---|---|
| `Profilo` | Impostazioni di un cliente: matrice per tipo, whitelist, blacklist. |
| `MappaCliente` | Nome originale ↔ nome anonimo, per ruolo, case-insensitive, con contatori per prefisso. I nomi restano stabili; "rigenera nomi" la azzera. Password e SID non ci entrano mai. |
| `ArchivioProfili` | Salva/carica profilo e mappa in JSON cifrato (tramite `IProtezioneDati`). |
| `ListeBase` | Oggetti di sistema (built-in, `sp_*`/`xp_*`, DB di sistema, `sys`, `INFORMATION_SCHEMA`, DMV), parole chiave T-SQL, whitelist base di parole generiche (es. `Data`, `Id`, `Codice`, `Stato`…). |
| `Classificatore` | Visitatore dell'albero ScriptDom: assegna il ruolo a ogni token di nome; ricava i tipi da DDL, `DECLARE`, parametri, `CAST`/`CONVERT`. |
| `GeneratoreNomi` | Crea i nomi parlanti (`tbl_001`, `col_dec18_2_007`, `@p_vc20_002`, `#tmp_003`…) e controlla che non collidano con parole chiave o nomi reali. |
| `GestoreCommenti` | `--` e `/* */` → `/* c001 */`, testo originale conservato per il ripristino. |
| `AnalizzatoreStringhe` | Dentro i letterali stringa: SQL dinamico completo → analizzato di nuovo in modo ricorsivo (apici raddoppiati, anche `@command` di `sp_add_jobstep`); frammenti → sostituzione per dizionario + avviso; percorsi UNC/locali, IP, host, email, URL → anonimizzati mantenendo struttura ed estensione. |
| `GestoreSqlcmd` | Righe `:setvar` / `:connect` trattate a parte; `GO` lasciato a ScriptDom. |
| `Anonimizzatore` | Punto d'ingresso: riceve uno o più script + profilo + mappa, restituisce testi anonimizzati, avvisi e legenda. Se lo script ha errori di sintassi passa alla **modalità di riserva** (solo token + dizionario) con avviso. |
| `GeneratoreLegenda` | Testo in testa che spiega la convenzione dei nomi all'IA; compone il testo unico da copiare (`-- Script 1`, `-- Script 2`…). |
| `DeAnonimizzatore` | Ripristina i nomi nel testo libero; report dei token senza corrispondenza e dei segnaposto commento persi. |
| `ControlloAntiFuga` | Cerca nell'output i nomi originali anonimizzati e le parole in blacklist; ri-analizza il risultato per verificare che sia ancora T-SQL valido. Restituisce l'esito: in fase 2 la copia resterà bloccata finché l'utente non conferma. |

## 4. Ordine di lavoro (tappe)

Ogni tappa finisce con test verdi e un commit. Dopo ogni tappa posso fermarmi a mostrarti il risultato.

1. **Scheletro**: soluzione, due progetti, primo test che analizza uno script con ScriptDom.
2. **Mappa e nomi**: `MappaCliente`, `GeneratoreNomi`, salvataggio JSON (con cifratura finta nei test).
3. **Nomi oggetti (livello 1)**: tabelle, viste, SP, funzioni, trigger, schemi, database, server, nomi in più parti,
   parentesi quadre, oggetti di sistema, `tbl_` di riserva. Prima prova di andata e ritorno
   (anonimizza → de-anonimizza = testo identico all'originale).
4. **Colonne, variabili, alias, indici, constraint**: in chiaro di default, eccezioni da blacklist, tipi ricavati
   dallo script, nomi composti (`FK_Clienti_Ordini` → `FK_tbl_001_tbl_002`).
5. **Whitelist / blacklist** per nome intero e per parole (CamelCase/underscore).
6. **Commenti (livello 5)** con segnaposto ripristinabili.
7. **Sicurezza (livello 2)**: login, utenti, ruoli, `[DOMINIO\utente]`, password e SID come segnaposto non salvati.
8. **Infrastruttura (livello 3)**: percorsi, IP, host, email, URL nelle stringhe (backup/restore, linked server, job).
9. **SQL dinamico** ricorsivo e frammenti concatenati; `sp_add_jobstep`.
10. **SQLCMD e modalità di riserva** per script con errori.
11. **Legenda e testo unico** da copiare con più script.
12. **De-anonimizzazione su testo libero** (nomi dentro la prosa, nomi composti inventati dall'IA, con/senza parentesi,
    maiuscole diverse) e report.
13. **Controllo anti-fuga**.
14. **Giro con i tuoi script realistici**: tu li metti in `prove-locali/` sul tuo PC (esclusa da git), li proviamo,
    e per ogni problema trovato scrivo un test con un esempio **inventato** che riproduce il caso.

## 5. Come verifichiamo

- **Test unitari** per ogni componente.
- **Test di andata e ritorno** su ogni script di esempio: de-anonimizzare l'output deve ridare esattamente l'originale.
- **Test anti-fuga**: nessun nome anonimizzato deve comparire nell'output.
- **Test di validità**: l'output deve essere ancora T-SQL analizzabile da ScriptDom.
- Script di esempio inventati per tipo: query, SP con SQL dinamico, DDL con FK e indici, script di sicurezza,
  backup/restore, job di SQL Agent, script con errori, script SQLCMD.

## 6. Cosa resta fuori dalla fase 1

- Interfaccia, appunti, editor Monaco (fase 2).
- La **query di import dello schema** e la sua schermata (fase 2). Nella fase 1 il motore accetta già uno schema
  "in memoria" (tabelle, colonne, tipi, PK, FK, indici) e sa produrre lo **schema anonimizzato allegato**, così la
  fase 2 deve solo leggerlo dal DB.
- Livello 4 (letterali/dati), salvo l'eventuale avviso leggero su email/CF/IBAN.
- Pacchetto zip e controllo WebView2 (fase 3).

## 7. Domande per Mattia (prima di partire)

1. **Numerazione delle colonne**: propongo un **unico contatore per tutte le colonne** (`col_vc50_001`,
   `col_int_002`, `col_x_003`…), così il numero da solo identifica la colonna e la de-anonimizzazione funziona anche se
   l'IA "sbaglia" la parte del tipo. Il tipo nel nome resta quello noto la prima volta (nomi stabili). Va bene?
2. **Contatori degli oggetti**: uno per prefisso (`tbl_001`, `vw_001`, `usp_001` partono tutti da 1). Va bene?
3. **Indici e constraint** il cui nome contiene parti anonimizzate: propongo di sostituire solo le parti
   (`IX_Ordini_Data` → `IX_tbl_002_Data`) invece di rinominarli tutti in `ix_001`, così l'IA capisce a cosa servono.
   `ix_001`/`pk_001` si usano solo quando il nome non si può scomporre. Va bene?
4. **Sviluppo**: la fase 1 la sviluppo e la testo qui nell'ambiente cloud (Linux, il motore non dipende da Windows).
   Sul tuo PC basterà fare `git pull` e potrai lanciare gli stessi test con `dotnet test`. Va bene?
