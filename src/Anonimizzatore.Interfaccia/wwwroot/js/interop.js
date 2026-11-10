// Piccole funzioni del browser usate dall'interfaccia. Nessuna chiamata di rete.

const CHIAVE_TEMA = 'anonimizzatore.tema';

/** Copia un testo negli appunti. Restituisce true se è riuscito. */
export async function copiaNegliAppunti(testo) {
  try {
    await navigator.clipboard.writeText(testo);
    return true;
  } catch {
    // Se l'accesso diretto non è consentito si ripiega su un campo temporaneo.
    const campo = document.createElement('textarea');
    campo.value = testo;
    campo.setAttribute('readonly', '');
    campo.style.position = 'fixed';
    campo.style.opacity = '0';
    document.body.appendChild(campo);
    campo.select();
    const ok = document.execCommand('copy');
    campo.remove();
    return ok;
  }
}

/** Il testo negli appunti (può chiedere il permesso). null se non è leggibile. */
export async function leggiDagliAppunti() {
  try {
    return await navigator.clipboard.readText();
  } catch {
    return null;
  }
}

/** Il tema salvato: 'sistema', 'light', 'dark' o 'contrast'. */
export function temaSalvato() {
  try {
    return localStorage.getItem(CHIAVE_TEMA) ?? 'sistema';
  } catch {
    return 'sistema';
  }
}

/** Applica il tema alla pagina e lo salva. 'sistema' segue le impostazioni del PC. */
export function impostaTema(tema) {
  const radice = document.documentElement;
  if (tema === 'sistema') radice.removeAttribute('data-theme');
  else radice.setAttribute('data-theme', tema);
  try {
    localStorage.setItem(CHIAVE_TEMA, tema);
  } catch {
    /* senza memoria locale il tema vale solo per questa sessione */
  }
  // L'editor ha i suoi colori: si rifanno dai token del tema appena applicato.
  import('./editor.js').then((m) => m.applicaTema()).catch(() => {});
}

// Il tema salvato si applica subito, prima che la pagina si disegni: niente lampo del tema sbagliato.
impostaTema(temaSalvato());

// Se il PC cambia tema mentre l'app è aperta e si segue il sistema, anche l'editor si adegua.
window.matchMedia?.('(prefers-color-scheme: dark)').addEventListener('change', () => {
  import('./editor.js').then((m) => m.applicaTema()).catch(() => {});
});
