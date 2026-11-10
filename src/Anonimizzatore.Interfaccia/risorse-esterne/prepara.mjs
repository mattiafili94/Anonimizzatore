// Porta nel progetto le risorse esterne, tutte locali (l'app non fa chiamate di rete):
//  - Monaco (editor e confronto): solo i file che servono, in wwwroot/lib/monaco
//  - icone Lucide: solo quelle usate, come codice C# in Componenti/IconeLucide.g.cs
// Uso:  npm ci  &&  npm run prepara

import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const qui = dirname(fileURLToPath(import.meta.url));
const progetto = join(qui, '..');
const monaco = join(qui, 'node_modules', 'monaco-editor');
const lucide = join(qui, 'node_modules', 'lucide-static');

function copia(da, a) {
  mkdirSync(dirname(a), { recursive: true });
  copyFileSync(da, a);
}

// --- Monaco ---------------------------------------------------------------------------------------------------
const daCopiare = [
  'min/vs/loader.js',
  'min/vs/editor/editor.main.js',
  'min/vs/editor/editor.main.css',
  'min/vs/base/worker/workerMain.js',
  'min/vs/nls.messages.it.js',
  'min/vs/base/browser/ui/codicons/codicon/codicon.ttf',
  'LICENSE',
  'ThirdPartyNotices.txt',
];
for (const file of daCopiare) {
  const da = join(monaco, file);
  if (!existsSync(da)) throw new Error(`Monaco: manca ${file}`);
  const a = join(progetto, 'wwwroot', 'lib', 'monaco', file.replace(/^min\//, ''));
  copia(da, a);
}
const versioneMonaco = JSON.parse(readFileSync(join(monaco, 'package.json'), 'utf8')).version;
writeFileSync(join(progetto, 'wwwroot', 'lib', 'monaco', 'VERSIONE.txt'), `monaco-editor ${versioneMonaco} (licenza MIT, vedi LICENSE)\n`);

// --- icone Lucide ---------------------------------------------------------------------------------------------
// Una riga per concetto: serve a trovare l'icona giusta (tabella "un concetto = un'icona" di UI Top).
const icone = [
  'arrow-left', 'x', 'plus', 'pencil', 'trash-2', 'search', 'copy', 'upload', 'download', 'check',
  'circle-check', 'triangle-alert', 'circle-x', 'info', 'loader-circle', 'settings', 'chevron-right',
  'chevron-down', 'chevron-up', 'refresh-cw', 'eye', 'eye-off', 'user', 'users', 'shield-check',
  'rotate-ccw', 'database', 'key-round', 'undo-2', 'save', 'external-link', 'file-text', 'list-filter',
  'ellipsis', 'sun', 'moon', 'contrast', 'monitor', 'clipboard-paste', 'lock', 'columns-2', 'panel-left',
];

const righe = [];
for (const nome of icone) {
  const file = join(lucide, 'icons', `${nome}.svg`);
  if (!existsSync(file)) throw new Error(`Lucide: manca l'icona ${nome}`);
  const svg = readFileSync(file, 'utf8');
  const dentro = svg.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>[\s\S]*$/, '').replace(/\s+/g, ' ').trim();
  righe.push(`        ["${nome}"] = """${dentro}""",`);
}
const versioneLucide = JSON.parse(readFileSync(join(lucide, 'package.json'), 'utf8')).version;
writeFileSync(join(progetto, 'Componenti', 'IconeLucide.g.cs'),
`// FILE GENERATO da risorse-esterne/prepara.mjs: non modificare a mano.
// Icone Lucide ${versioneLucide} (licenza ISC, Copyright Lucide Icons and Contributors), solo quelle usate.
namespace Anonimizzatore.Interfaccia.Componenti;

internal static class IconeLucide
{
    /// <summary>Il contenuto dell'SVG (24x24, contorno) di ogni icona, per nome Lucide.</summary>
    public static readonly IReadOnlyDictionary<string, string> Contenuto = new Dictionary<string, string>
    {
${righe.join('\n')}
    };
}
`);
copia(join(lucide, 'LICENSE'), join(progetto, 'wwwroot', 'lib', 'lucide-LICENSE.txt'));

console.log(`Monaco ${versioneMonaco}: ${daCopiare.length} file. Lucide ${versioneLucide}: ${icone.length} icone.`);
