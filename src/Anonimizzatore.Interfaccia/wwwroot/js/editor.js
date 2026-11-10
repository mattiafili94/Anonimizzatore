// Monaco, tutto locale: nessuna chiamata di rete. Editor di T-SQL e confronto affiancato.
// I colori vengono dai token di UI Top (variabili CSS), quindi seguono i tre temi.

const radice = new URL('_content/Anonimizzatore.Interfaccia/lib/monaco/vs', document.baseURI).href;

let promessaMonaco = null;
const editor = new Map(); // id -> { editor | diff, modelli, riferimento }
let prossimoId = 1;

// ---------------------------------------------------------------------------------------------------------------
// Caricamento

function caricaMonaco() {
  if (promessaMonaco) return promessaMonaco;

  promessaMonaco = new Promise((risolvi, rifiuta) => {
    const css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = radice + '/editor/editor.main.css';
    document.head.appendChild(css);

    const script = document.createElement('script');
    script.src = radice + '/loader.js';
    script.onerror = () => rifiuta(new Error('Impossibile caricare l\'editor.'));
    script.onload = () => {
      // Il lavoro pesante (confronto, calcoli) va in un worker creato da un blob: così resta tutto locale.
      window.MonacoEnvironment = {
        getWorkerUrl: () => {
          const codice = `self.MonacoEnvironment = { baseUrl: '${radice}/' }; importScripts('${radice}/base/worker/workerMain.js');`;
          return URL.createObjectURL(new Blob([codice], { type: 'text/javascript' }));
        },
      };
      window.require.config({ paths: { vs: radice }, 'vs/nls': { availableLanguages: { '*': 'it' } } });
      window.require(['vs/editor/editor.main'], () => {
        registraTsql(window.monaco);
        applicaTema();
        risolvi(window.monaco);
      }, rifiuta);
    };
    document.head.appendChild(script);
  });
  return promessaMonaco;
}

// ---------------------------------------------------------------------------------------------------------------
// Il linguaggio T-SQL. Monaco non ha una grammatica per T-SQL: questa evidenzia anche i nostri nomi anonimi
// (tbl_001, col_vc50_001...) e i segnaposto (/* c001 */, <password>).

const PAROLE_CHIAVE = (
  'add all alter and any as asc authorization backup begin between break browse bulk by cascade case catch check ' +
  'checkpoint close clustered coalesce collate column commit compute constraint contains continue convert create cross ' +
  'current cursor database dbcc deallocate declare default delete deny desc disk distinct distributed drop dump else end ' +
  'except exec execute exists exit external fetch file fillfactor for foreign freetext from full function go goto grant ' +
  'group having holdlock identity if in index inner insert intersect into is join key kill left like merge national ' +
  'nocheck nonclustered not null nullif of off offsets on open option or order outer over percent pivot plan primary ' +
  'print proc procedure public raiserror read readtext reconfigure references replication restore restrict return ' +
  'revert revoke right rollback rowcount rule save schema select set setuser shutdown some statistics table tablesample ' +
  'textsize then throw top tran transaction trigger truncate try unique union unpivot update updatetext use user values ' +
  'varying view waitfor when where while with writetext login role certificate credential job output outer apply ' +
  'cross nolock rowlock updlock readuncommitted identity_insert enable disable rebuild reorganize partition filegroup ' +
  'recovery simple bulk_logged compression init noinit format stats move replace norecovery standby'
).split(' ');

const TIPI = (
  'int bigint smallint tinyint bit decimal numeric money smallmoney float real date datetime datetime2 datetimeoffset ' +
  'smalldatetime time char varchar text nchar nvarchar ntext binary varbinary image uniqueidentifier xml sql_variant ' +
  'sysname rowversion timestamp hierarchyid geography geometry json'
).split(' ');

function registraTsql(monaco) {
  if (monaco.languages.getLanguages().some((l) => l.id === 'tsql')) return;
  monaco.languages.register({ id: 'tsql', extensions: ['.sql'], aliases: ['T-SQL', 'tsql'] });

  const nomeAnonimo = /\b(?:srv|db|sch|dom|tbl|vw|usp|fn|trg|usr|rol|job|ix|pk|fk|df|ck|uq|al|dir|file|ip)_\d+\b/;
  const colonnaAnonima = /\bcol_[a-z][a-z0-9]*(?:_\d+)?_\d{3,}\b/;
  const variabileAnonima = /@[vp]_[a-z][a-z0-9]*(?:_\d+)?_\d{3,}\b/;
  const temporanea = /##?tmp_\d+\b/;
  // In una grammatica Monarch "@nome" è un riferimento a un attributo: per una chiocciola vera si scrive "@@".
  const emailAnonima = /\bmail_\d+@@example\.invalid\b/;

  monaco.languages.setMonarchTokensProvider('tsql', {
    defaultToken: '',
    ignoreCase: true,
    keywords: PAROLE_CHIAVE,
    tipi: TIPI,
    tokenizer: {
      root: [
        [/\/\*\s*c\d{3,}\s*\*\//, 'segnaposto'],
        [/<password>/, 'segnaposto'],
        [/--.*$/, 'comment'],
        [/\/\*/, 'comment', '@commento'],
        [emailAnonima, 'nome-anonimo'],
        [nomeAnonimo, 'nome-anonimo'],
        [colonnaAnonima, 'nome-anonimo'],
        [variabileAnonima, 'nome-anonimo'],
        [temporanea, 'nome-anonimo'],
        [/N?'/, 'string', '@stringa'],
        [/\[/, 'identifier.quote', '@parentesi'],
        [/@@(?:@@)?[\w$#]+/, 'variable'],
        [/#{1,2}[\w$]+/, 'variable'],
        [/0x[0-9a-f]*/, 'number'],
        [/\d+(\.\d+)?/, 'number'],
        [/[a-z_][\w$#]*/, { cases: { '@tipi': 'type', '@keywords': 'keyword', '@default': 'identifier' } }],
        [/[;,.]/, 'delimiter'],
        [/[()=<>!+\-*\/%&|^~]+/, 'operator'],
      ],
      commento: [
        [/[^/*]+/, 'comment'],
        [/\/\*/, 'comment', '@push'],
        [/\*\//, 'comment', '@pop'],
        [/[/*]/, 'comment'],
      ],
      stringa: [
        [/[^']+/, 'string'],
        [/''/, 'string.escape'],
        [/'/, 'string', '@pop'],
      ],
      parentesi: [
        [nomeAnonimo, 'nome-anonimo'],
        [colonnaAnonima, 'nome-anonimo'],
        [/[\w$@]+/, 'identifier.quote'],
        [/\]\]/, 'identifier.quote'],
        [/\]/, 'identifier.quote', '@pop'],
        [/[^\]\w$@]+/, 'identifier.quote'],
      ],
    },
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Temi: i colori sono quelli dei token, letti dal CSS, così editor e interfaccia non si possono disallineare.

function variabile(nome) {
  return getComputedStyle(document.documentElement).getPropertyValue(nome).trim();
}

/** "#rrggbb" + trasparenza (0-255) -> "#rrggbbaa"; qualunque altro formato resta com'è. */
function conAlfa(colore, alfa) {
  return /^#[0-9a-f]{6}$/i.test(colore) ? colore + alfa.toString(16).padStart(2, '0') : colore;
}

export function applicaTema() {
  const monaco = window.monaco;
  if (!monaco) return;

  const scuro = getComputedStyle(document.documentElement).colorScheme.includes('dark');
  const contrasto = document.documentElement.dataset.theme === 'contrast';
  const v = variabile;
  const senzaCancelletto = (c) => c.replace('#', '');

  monaco.editor.defineTheme('anonimizzatore', {
    base: scuro ? 'vs-dark' : 'vs',
    inherit: true,
    rules: [
      { token: '', foreground: senzaCancelletto(v('--text-primary')) },
      { token: 'keyword', foreground: senzaCancelletto(v('--accent-default')), fontStyle: 'bold' },
      { token: 'type', foreground: senzaCancelletto(v('--chart-3')) },
      { token: 'string', foreground: senzaCancelletto(v('--status-success')) },
      { token: 'string.escape', foreground: senzaCancelletto(v('--status-success')), fontStyle: 'bold' },
      { token: 'number', foreground: senzaCancelletto(v('--status-warning')) },
      { token: 'comment', foreground: senzaCancelletto(v('--text-secondary')), fontStyle: 'italic' },
      { token: 'variable', foreground: senzaCancelletto(v('--chart-4')) },
      { token: 'identifier.quote', foreground: senzaCancelletto(v('--text-primary')) },
      { token: 'nome-anonimo', foreground: senzaCancelletto(v('--chart-2')), fontStyle: 'bold' },
      { token: 'segnaposto', foreground: senzaCancelletto(v('--text-secondary')), fontStyle: 'italic bold' },
      { token: 'delimiter', foreground: senzaCancelletto(v('--text-secondary')) },
      { token: 'operator', foreground: senzaCancelletto(v('--text-secondary')) },
    ],
    colors: {
      'editor.background': v('--background-surface'),
      'editor.foreground': v('--text-primary'),
      'editorLineNumber.foreground': v('--text-secondary'),
      'editorLineNumber.activeForeground': v('--text-primary'),
      'editorCursor.foreground': v('--accent-default'),
      'editor.selectionBackground': v('--state-selected'),
      'editor.inactiveSelectionBackground': v('--state-hover'),
      'editor.lineHighlightBackground': v('--state-hover'),
      'editor.lineHighlightBorder': '#00000000',
      'editorGutter.background': v('--background-surface'),
      'editorWidget.background': v('--background-elevated'),
      'editorWidget.border': v('--border-subtle'),
      'editorIndentGuide.background1': v('--border-subtle'),
      'scrollbarSlider.background': conAlfa(v('--text-secondary'), contrasto ? 0x99 : 0x55),
      'scrollbarSlider.hoverBackground': conAlfa(v('--text-secondary'), 0x88),
      'diffEditor.insertedTextBackground': conAlfa(v('--status-success'), 0x33),
      'diffEditor.removedTextBackground': conAlfa(v('--status-error'), 0x33),
      'diffEditor.insertedLineBackground': conAlfa(v('--status-success'), 0x1a),
      'diffEditor.removedLineBackground': conAlfa(v('--status-error'), 0x1a),
      'diffEditor.border': v('--border-subtle'),
    },
  });
  monaco.editor.setTheme('anonimizzatore');
}

// ---------------------------------------------------------------------------------------------------------------
// Editor singolo

const OPZIONI_COMUNI = {
  language: 'tsql',
  theme: 'anonimizzatore',
  automaticLayout: true,
  minimap: { enabled: false },
  scrollBeyondLastLine: false,
  fontFamily: variabile('--font-mono') || 'Consolas, monospace',
  fontSize: 13,
  lineHeight: 20,
  renderLineHighlight: 'none',
  overviewRulerLanes: 0,
  padding: { top: 8, bottom: 8 },
  wordWrap: 'off',
  tabSize: 4,
  guides: { indentation: false },
  accessibilitySupport: 'auto',
};

/** Crea un editor di T-SQL dentro l'elemento. Restituisce l'id da usare nelle altre funzioni. */
export async function creaEditor(elemento, valore, soloLettura, riferimento, etichetta) {
  const monaco = await caricaMonaco();
  const id = prossimoId++;

  const ed = monaco.editor.create(elemento, {
    ...OPZIONI_COMUNI,
    value: valore ?? '',
    readOnly: soloLettura,
    ariaLabel: etichetta ?? 'Editor',
  });
  let attesa = null;
  ed.onDidChangeModelContent(() => {
    // Un piccolo ritardo: durante la digitazione non si avvisa .NET a ogni tasto.
    clearTimeout(attesa);
    attesa = setTimeout(() => riferimento.invokeMethodAsync('QuandoCambia', ed.getValue()), 150);
  });
  editor.set(id, { tipo: 'editor', ed });
  return id;
}

export function impostaValore(id, valore) {
  const voce = editor.get(id);
  if (voce?.tipo === 'editor' && voce.ed.getValue() !== valore) voce.ed.setValue(valore ?? '');
}

export function leggiValore(id) {
  const voce = editor.get(id);
  return voce?.tipo === 'editor' ? voce.ed.getValue() : '';
}

export function mettiAFuoco(id) {
  const voce = editor.get(id);
  if (voce?.tipo === 'editor') voce.ed.focus();
}

// ---------------------------------------------------------------------------------------------------------------
// Confronto affiancato

export async function creaConfronto(elemento, originale, modificato) {
  const monaco = await caricaMonaco();
  const id = prossimoId++;

  const diff = monaco.editor.createDiffEditor(elemento, {
    ...OPZIONI_COMUNI,
    readOnly: true,
    originalEditable: false,
    renderSideBySide: true,
    enableSplitViewResizing: true,
    renderOverviewRuler: false,
    ignoreTrimWhitespace: false,
    renderIndicators: true,
    useInlineViewWhenSpaceIsLimited: false,
  });
  const modelloOriginale = monaco.editor.createModel(originale ?? '', 'tsql');
  const modelloModificato = monaco.editor.createModel(modificato ?? '', 'tsql');
  diff.setModel({ original: modelloOriginale, modified: modelloModificato });
  editor.set(id, { tipo: 'confronto', diff, modelli: [modelloOriginale, modelloModificato] });
  return id;
}

export function aggiornaConfronto(id, originale, modificato) {
  const voce = editor.get(id);
  if (voce?.tipo !== 'confronto') return;
  const [o, m] = voce.modelli;
  if (o.getValue() !== originale) o.setValue(originale ?? '');
  if (m.getValue() !== modificato) m.setValue(modificato ?? '');
}

// ---------------------------------------------------------------------------------------------------------------

export function distruggi(id) {
  const voce = editor.get(id);
  if (!voce) return;
  if (voce.tipo === 'editor') {
    voce.ed.getModel()?.dispose();
    voce.ed.dispose();
  } else {
    voce.diff.dispose();
    voce.modelli.forEach((m) => m.dispose());
  }
  editor.delete(id);
}
