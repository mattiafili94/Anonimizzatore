# Piano della fase 2 — Interfaccia

Stato: **in corso** (10/11/2026): 2A-2E fatte, 2F in parte. Le schermate sono una proposta: si cambiano facilmente, il motore non si tocca.
Design: **UI Top** (stesso design system di Arco, `../Arco/design-system/`, artifact BEUsaG1jRRShkG7r6DdHXN).

## 1. Identità del prodotto (le sei righe di UI Top)

```text
Product character: strumento locale e riservato per chi lavora su SQL Server; sobrio, preciso, che dà fiducia perché mostra sempre cosa è stato cambiato e cosa è rimasto
Density: balanced (compact negli editor: è uno strumento da esperti)
Geometry: precise
Accent behavior: restrained (il blu solo per l'azione principale, la selezione e il focus)
Motion character: precise
Primary domain pattern: confronto affiancato originale / anonimizzato, e il testo da copiare
Distinctive visual behavior: nell'editor i nomi anonimi (tbl_001, col_vc50_001...) e i segnaposto (/* c001 */, <password>) hanno un tono a parte; gli avvisi stanno accanto al punto interessato
```

## 2. Struttura

```
src/
  Anonimizzatore.Motore/           (fase 1, invariato)
  Anonimizzatore.Interfaccia/      libreria Razor: componenti, pagine, servizi, CSS, Monaco, icone
  Anonimizzatore.App/              eseguibile WPF: finestra con BlazorWebView (WebView2), DPAPI, cartella dei dati
tools/
  Anonimizzatore.Anteprima/        solo sviluppo: la stessa interfaccia in un browser, per vederla e provarla
tests/
  Anonimizzatore.Interfaccia.Test/ test dei servizi e dei componenti (bUnit)
```

- L'interfaccia è **uno strato sottile**: chiama `AnonimizzatoreScript.Anonimizza` → `TestoPerIA.Componi` →
  `ControlloAntiFuga.Verifica` all'andata e `DeAnonimizzatore.DeAnonimizza` al ritorno.
- Quello che cambia tra l'app e l'anteprima sta dietro due piccole cose: la **cifratura** dei profili (`IProtezioneDati`:
  DPAPI nell'app, finta nell'anteprima) e la **cartella dei dati** (`%APPDATA%\Anonimizzatore` nell'app, una cartella temporanea
  nell'anteprima).
- **Nessuna chiamata di rete**: Monaco, icone e CSS sono file locali (`wwwroot`); l'anteprima è solo per sviluppo e non si distribuisce.

## 3. Schermate

Barra laterale a sinistra (da `bp-medium`; sotto, barra in basso), quattro destinazioni:

| Destinazione | Icona | A cosa serve |
|---|---|---|
| **Anonimizza** | `shield-check` | Scegli il profilo, incolli uno o più script, vedi il confronto, copi il testo per l'IA |
| **Ripristina** | `rotate-ccw` | Incolli la risposta dell'IA, vedi il testo con i nomi veri e gli avvisi, lo copi |
| **Profili** | `users` | Un profilo per cliente: nome, regole per tipo di oggetto, whitelist, blacklist, schema, nomi assegnati |
| **Impostazioni** | `settings` | Tema (chiaro, scuro, alto contrasto, come il sistema), versione, cartella dei dati |

### Anonimizza (la schermata principale)
1. In alto il **profilo** (riga nome-valore: apre l'elenco dei profili) e il pulsante principale **Anonimizza**.
2. Gli script: un editor per ognuno (titolo "Script 1", "Script 2"), **Aggiungi script**, rimuovi. Incolla e basta.
3. Dopo Anonimizza, tre viste con un controllo segmentato: **Confronto** (originale a sinistra, anonimizzato a destra,
   un confronto per script), **Testo per l'IA** (esattamente ciò che verrà copiato: legenda, schema, script) e **Avvisi**.
4. **Copia testo per l'IA**: se il controllo anti-fuga trova qualcosa che blocca, si apre una finestra che elenca le fughe
   (con la riga) e chiede conferma ("Copia comunque" non è il pulsante di default).
5. Se si cambia lo script dopo aver anonimizzato, il risultato si segna come "non aggiornato" finché non si ripete Anonimizza.

### Ripristina
Un editor per la risposta intera dell'IA, **Ripristina nomi**, poi il risultato con il confronto (risposta / ripristinato),
gli **avvisi** del report (nomi senza corrispondenza, commenti persi, password da riempire) e **Copia risultato**.

### Profili
Elenco (nome, ultimo uso) con **Nuovo profilo**. Dettaglio: nome; **regole per tipo di oggetto** (righe nome-valore
"Anonimizza / In chiaro"); **whitelist** e **blacklist** (una voce per riga, con spiegazione di cosa fanno); **schema del
database** (incolla il risultato della query di import; la query la fornisce l'app); **nomi assegnati** (tabella
originale ↔ anonimo, per controllare); in fondo, separate: **Rigenera nomi** e **Elimina profilo** (con conferma che nomina
il profilo e la conseguenza).

### Impostazioni
Tema, versione (visibile anche nella barra laterale), cartella dei dati, informazioni su offline e riservatezza.

## 4. Tappe della fase 2

| Tappa | Contenuto |
|---|---|
| **2A** | Scheletro dei progetti, design system (token, base, tre temi), icone Lucide, Monaco offline (editor + confronto) con grammatica T-SQL, barra laterale, anteprima nel browser |
| **2B** | Profili: elenco, creazione, scelta; salvataggio cifrato; regole per tipo, whitelist, blacklist; rigenera nomi; elimina |
| **2C** | Anonimizza end-to-end: script, confronto, testo per l'IA, avvisi, controllo anti-fuga con conferma, copia |
| **2D** | Ripristina end-to-end con il report |
| **2E** | Schema del database: query di import, incolla il risultato, tabella dei nomi assegnati |
| **2F** | Rifinitura: scorciatoie da tastiera, stati vuoti e di errore, accessibilità (tastiera, contrasto nei tre temi), test dei componenti |

## 5. Decisioni

- **Monaco 0.52.2** con il solo necessario (editor, confronto, traduzione italiana, font delle icone dell'editor): circa 4,5 MB.
  Versioni più nuove hanno un formato a file spezzettati poco adatto a un'app offline. Non ha un linguaggio T-SQL: la
  grammatica è nostra (`tsql`), con i nomi anonimi e i segnaposto in evidenza.
- **Icone Lucide** (come Arco), estratte dal pacchetto e incluse come SVG nel codice; tabella "un concetto = un'icona" in
  `src/Anonimizzatore.Interfaccia/Componenti/Icona.razor`. Concetti propri: Anonimizza `shield-check`, Ripristina `rotate-ccw`,
  Profili `users`, Schema `database`, Password `key-round`.
- **Token**: copia adattata di quelli di UI Top (accento di default `#1d5bd6`, niente colori del bersaglio di Arco), tre temi.
- **Glossario**: Profilo (non "cliente"), Script, Anonimizza, Ripristina, Nome anonimo, Legenda, Schema, Avviso, Mappa.
  Etichette in italiano con la sola iniziale maiuscola.
- Le risorse esterne (Monaco, Lucide) si prendono con `npm` da `src/Anonimizzatore.Interfaccia/risorse-esterne/` e il risultato
  **sta nella repo**: chi clona e fa `dotnet build` non ha bisogno di Node.
