using System.Text;
using System.Text.RegularExpressions;
using Anonimizzatore.Motore.Mappa;

namespace Anonimizzatore.Motore.Infrastruttura;

/// <summary>
/// Livello 3 (infrastruttura), parte "pura testo": trova e sostituisce in un testo qualsiasi (il contenuto di un
/// letterale stringa o un frammento di testo libero) percorsi UNC e locali, indirizzi IPv4, host/FQDN, email, URL e i
/// server delle stringhe di connessione. Non conosce ScriptDom: l'aggancio alle stringhe dello script è della tappa 9.
/// <list type="bullet">
/// <item>Struttura ed estensione dei percorsi restano: <c>\\SRVBACKUP\Condivisa\Vendite.bak</c> →
/// <c>\\srv_01\dir_001\db_01.bak</c>. Le cartelle di sistema (<c>Windows</c>, <c>Program Files</c>, <c>MSSQL</c>,
/// <c>DATA</c>, <c>Backup</c>…) e le parole generiche (<c>FULL</c>, date…) restano in chiaro.</item>
/// <item>Un segmento che coincide con un server/database/dominio già in mappa prende il suo nome anonimo
/// (<c>Vendite_FULL_20260101.bak</c> → <c>db_01_FULL_20260101.bak</c>); altrimenti diventa <c>dir_NNN</c> o
/// <c>file_NNN</c>.</item>
/// <item>I nomi sono stabili grazie alla <see cref="MappaCliente"/>; <see cref="Ripristina"/> fa il percorso inverso.</item>
/// </list>
/// Limiti noti: solo IPv4 (niente IPv6); un nome di server "da solo" nel testo (<c>SRVSQL01</c>) non è riconoscibile,
/// lo è solo dove il contesto lo dice (UNC, URL, stringa di connessione); il ripristino restituisce l'ortografia
/// con cui un nome è stato visto la prima volta (la mappa confronta senza maiuscole/minuscole).
/// </summary>
public sealed partial class AnalizzatoreInfrastruttura
{
    private readonly MappaCliente _mappa;
    private readonly OpzioniInfrastruttura _opzioni;

    private IReadOnlyList<VoceMappa> _noti = [];
    private int _contoVociNoti = -1;

    public AnalizzatoreInfrastruttura(MappaCliente mappa, OpzioniInfrastruttura? opzioni = null)
    {
        _mappa = mappa ?? throw new ArgumentNullException(nameof(mappa));
        _opzioni = opzioni ?? new OpzioniInfrastruttura();
    }

    // ───────────────────────────── Anonimizzazione ─────────────────────────────

    public RisultatoInfrastruttura Anonimizza(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);
        var piano = new Piano();

        // L'ordine conta: ciò che è trovato prima "prenota" il suo intervallo e i rilevatori successivi lo saltano.
        TrovaUrl(testo, piano);
        TrovaEmail(testo, piano);
        TrovaPercorsiUnc(testo, piano);
        TrovaPercorsiLocali(testo, piano);
        TrovaPercorsiUnix(testo, piano);
        TrovaStringheConnessione(testo, piano);
        TrovaIp(testo, piano);
        TrovaHost(testo, piano);

        var sb = new StringBuilder(testo.Length);
        var sostituzioni = new List<Sostituzione>();
        var cursore = 0;
        foreach (var pezzo in piano.Pezzi.OrderBy(p => p.Inizio))
        {
            sb.Append(testo, cursore, pezzo.Inizio - cursore).Append(pezzo.Nuovo);
            cursore = pezzo.Fine;
            var originale = testo[pezzo.Inizio..pezzo.Fine];
            if (!originale.Equals(pezzo.Nuovo, StringComparison.Ordinal))
                sostituzioni.Add(new Sostituzione(pezzo.Tipo, originale, pezzo.Nuovo, pezzo.Inizio));
        }
        sb.Append(testo, cursore, testo.Length - cursore);

        return new RisultatoInfrastruttura(sb.ToString(), sostituzioni, piano.Avvisi);
    }

    // ───────────────────────────── Ripristino ─────────────────────────────

    /// <summary>
    /// Inverso di <see cref="Anonimizza"/>: rimette gli originali al posto di <c>srv_01</c>, <c>db_01</c>, <c>dom_01</c>,
    /// <c>dir_001</c>, <c>file_001</c>, <c>ip_001</c> e <c>mail_001@example.invalid</c>, anche dentro nomi composti
    /// (<c>db_01_FULL.bak</c>) e senza badare alle maiuscole.
    /// </summary>
    public RisultatoRipristino Ripristina(string testoAnonimo)
    {
        ArgumentNullException.ThrowIfNull(testoAnonimo);
        var sostituzioni = new List<Sostituzione>();
        var nonTrovati = new List<string>();

        var testo = NomeAnonimo().Replace(testoAnonimo, m =>
        {
            var voce = _mappa.TrovaOriginale(m.Value);
            if (voce is null || !FamiglieRipristinabili.Contains(FamigliaRuolo.Di(voce.Ruolo)))
            {
                if (!nonTrovati.Contains(m.Value, NomeSql.Confronto)) nonTrovati.Add(m.Value);
                return m.Value;
            }
            sostituzioni.Add(new Sostituzione(TipoDi(voce.Ruolo), m.Value, voce.Originale, m.Index));
            return voce.Originale;
        });

        return new RisultatoRipristino(testo, sostituzioni, nonTrovati);
    }

    private static readonly HashSet<string> FamiglieRipristinabili = ["srv", "db", "dom", "dir", "file", "ip", "mail"];

    private static TipoInfrastruttura TipoDi(Ruolo ruolo) => ruolo switch
    {
        Ruolo.Email => TipoInfrastruttura.Email,
        Ruolo.Ip => TipoInfrastruttura.Ip,
        Ruolo.Server or Ruolo.LinkedServer or Ruolo.Dominio => TipoInfrastruttura.Host,
        _ => TipoInfrastruttura.PercorsoLocale,
    };

    // ───────────────────────────── URL ─────────────────────────────

    private void TrovaUrl(string t, Piano piano)
    {
        foreach (Match m in Url().Matches(t))
        {
            var host = m.Groups["host"].Value;
            var porta = m.Groups["porta"].Value;
            var resto = m.Groups["resto"].Value;

            var restoPulito = resto.TrimEnd('.', ',', ';', ')', '!');
            var tolti = resto.Length - restoPulito.Length;
            if (restoPulito.Length == 0 && porta.Length == 0)
            {
                var hostPulito = host.TrimEnd('.');
                tolti += host.Length - hostPulito.Length;
                host = hostPulito;
            }
            if (host.Length == 0) continue;

            piano.Aggiungi(m.Index, m.Index + m.Length - tolti, TipoInfrastruttura.Url, () =>
            {
                var sb = new StringBuilder();
                sb.Append(m.Groups["schema"].Value).Append("://");
                if (m.Groups["utente"].Success)
                {
                    sb.Append(_mappa.Ottieni(Ruolo.Utente, m.Groups["utente"].Value));
                    if (m.Groups["pwd"].Success)
                    {
                        sb.Append(":***");
                        piano.Avvisi.Add("URL con password: sostituita da *** e NON salvata nella mappa, quindi non verrà ripristinata.");
                    }
                    sb.Append('@');
                }
                sb.Append(MappaHost(host));
                if (porta.Length > 0) sb.Append(':').Append(porta);
                sb.Append(restoPulito);
                if (restoPulito.Length > 1)
                    piano.Avvisi.Add($"URL con percorso o parametri lasciati in chiaro: controlla che non contengano nomi sensibili ({m.Value.TrimEnd('.', ',', ';', ')', '!')}).");
                return sb.ToString();
            });
        }
    }

    // ───────────────────────────── Email ─────────────────────────────

    private void TrovaEmail(string t, Piano piano)
    {
        foreach (Match m in Email().Matches(t))
        {
            if (m.Groups["d"].Value.Equals("example.invalid", StringComparison.OrdinalIgnoreCase)) continue; // già anonima
            piano.Aggiungi(m.Index, m.Index + m.Length, TipoInfrastruttura.Email,
                () => _mappa.Ottieni(Ruolo.Email, m.Value));
        }
    }

    // ───────────────────────────── Percorsi ─────────────────────────────

    private void TrovaPercorsiUnc(string t, Piano piano)
    {
        foreach (Match m in InizioUnc().Matches(t))
        {
            var fine = FinePercorso(t, m.Index, 2);
            if (fine <= m.Index + 2) continue;
            piano.Aggiungi(m.Index, fine, TipoInfrastruttura.PercorsoUnc,
                () => AnonimizzaPercorso(t[m.Index..fine], unc: true, conUnita: false));
        }
    }

    private void TrovaPercorsiLocali(string t, Piano piano)
    {
        foreach (Match m in InizioLocale().Matches(t))
        {
            var fine = FinePercorso(t, m.Index, 2);
            if (fine <= m.Index + 3) continue;
            piano.Aggiungi(m.Index, fine, TipoInfrastruttura.PercorsoLocale,
                () => AnonimizzaPercorso(t[m.Index..fine], unc: false, conUnita: true));
        }
    }

    private void TrovaPercorsiUnix(string t, Piano piano)
    {
        foreach (Match m in InizioUnix().Matches(t))
        {
            var fine = FinePercorso(t, m.Index, 1);
            if (fine <= m.Index + 1) continue;
            piano.Aggiungi(m.Index, fine, TipoInfrastruttura.PercorsoLocale,
                () => AnonimizzaPercorso(t[m.Index..fine], unc: false, conUnita: false));
        }
    }

    /// <summary>
    /// Dove finisce il percorso che inizia in <paramref name="inizio"/> (<paramref name="prefisso"/> = caratteri già
    /// letti: <c>C:</c>, <c>\\</c> o <c>/</c>). In modalità letterale gli spazi fanno parte del nome; in testo libero
    /// lo chiudono.
    /// </summary>
    private int FinePercorso(string t, int inizio, int prefisso)
    {
        var i = inizio + prefisso;
        var inizioSegmento = i;
        while (i < t.Length)
        {
            var c = t[i];
            if (c is '\\' or '/') { i++; inizioSegmento = i; continue; }
            if (c is '"' or '<' or '>' or '|' or '*' or '?' or '\r' or '\n' or '\t') break;
            if (c == '\'')
            {
                // apostrofo dentro un nome (L'Aquila): resta; altrimenti è un apice che chiude la stringa
                var dentroAlNome = i > inizioSegmento && char.IsLetter(t[i - 1]) && i + 1 < t.Length && char.IsLetter(t[i + 1]);
                if (!dentroAlNome) break;
                i++;
                continue;
            }
            if (c == ':')
            {
                // "copy D:\a\b E:\x": la E: è l'unità del percorso successivo, non fa parte di questo
                if (i - 2 > inizio && char.IsLetter(t[i - 1]) && char.IsWhiteSpace(t[i - 2])) i -= 2;
                break;
            }
            if (c == ' ')
            {
                if (InizioAltroPercorso().IsMatch(t.AsSpan(i))) break;
                if (_opzioni.TestoLibero)
                {
                    var fineNome = FineNomeConSpazi(t, inizioSegmento, i);
                    if (fineNome <= i) break;
                    i = fineNome;
                    continue;
                }
            }
            i++;
        }

        // Percorso con spazi chiuso da un'estensione nota: "D:\a\b.bak WITH NOINIT" finisce dopo ".bak".
        var candidato = t[inizio..i];
        var ultimoSep = candidato.LastIndexOfAny(['\\', '/']);
        var ultimo = candidato[(ultimoSep + 1)..];
        if (ultimo.Contains(' '))
        {
            foreach (Match e in EstensioneSeguitaDaSpazio().Matches(ultimo))
            {
                if (!ElenchiInfrastruttura.EstensioniNote.Contains(e.Groups["e"].Value)) continue;
                candidato = candidato[..(ultimoSep + 1 + e.Index + e.Length)];
                break;
            }
        }

        candidato = candidato.TrimEnd(' ');
        if (_opzioni.TestoLibero)
        {
            candidato = candidato.TrimEnd('.', ',', ';', '\'');
            var ultimoLibero = candidato[(candidato.LastIndexOfAny(['\\', '/']) + 1)..];
            if (candidato.EndsWith(')') && !ultimoLibero.Contains('(')) candidato = candidato[..^1];
        }
        return inizio + candidato.Length;
    }

    private static int FineNomeConSpazi(string t, int inizioSegmento, int posizioneSpazio)
    {
        foreach (var nome in ElenchiInfrastruttura.CartelleConSpazi)
        {
            if (inizioSegmento + nome.Length > posizioneSpazio
                && string.Compare(t, inizioSegmento, nome, 0, nome.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return inizioSegmento + nome.Length;
        }
        return posizioneSpazio;
    }

    private string AnonimizzaPercorso(string p, bool unc, bool conUnita)
    {
        var sb = new StringBuilder();
        var i = 0;
        if (unc || conUnita)
        {
            sb.Append(p, 0, 2);
            i = 2;
        }

        var indice = 0;
        var dopoProfili = false;
        while (i < p.Length)
        {
            while (i < p.Length && EsSeparatore(p[i])) sb.Append(p[i++]);
            var j = i;
            while (j < p.Length && !EsSeparatore(p[j])) j++;
            if (j == i) break;

            var seg = p[i..j];
            string nuovo;
            if (unc && indice == 0) nuovo = MappaHost(seg);
            else if (unc && indice == 1 && ElenchiInfrastruttura.CondivisioneAmministrativa().IsMatch(seg)) nuovo = seg;
            else if (seg.Contains('%') || seg.Contains("$(")) nuovo = seg; // variabili d'ambiente / token di SQL Agent
            else if (dopoProfili)
                nuovo = ElenchiInfrastruttura.ProfiliGenerici.Contains(seg) ? seg : _mappa.Ottieni(Ruolo.Cartella, seg);
            else nuovo = ElaboraSegmento(seg, ultimo: j == p.Length);

            sb.Append(nuovo);
            dopoProfili = ElenchiInfrastruttura.CartelleProfili.Contains(seg);
            indice++;
            i = j;
        }
        return sb.ToString();
    }

    private string ElaboraSegmento(string seg, bool ultimo)
    {
        if (ultimo)
        {
            var (stem, estensione) = DividiEstensione(seg);
            if (estensione.Length > 0) return ElaboraNome(stem, Ruolo.File) + estensione;
        }
        return ElaboraNome(seg, Ruolo.Cartella);
    }

    private static (string Nome, string Estensione) DividiEstensione(string seg)
    {
        var punto = seg.LastIndexOf('.');
        if (punto <= 0) return (seg, "");
        var estensione = seg[punto..];
        if (estensione.Length is < 2 or > 9) return (seg, "");
        var resto = estensione.AsSpan(1);
        foreach (var c in resto) if (!char.IsAsciiLetterOrDigit(c)) return (seg, "");
        foreach (var c in resto) if (char.IsAsciiLetter(c)) return (seg[..punto], estensione);
        return (seg, ""); // ".2026": non è un'estensione
    }

    /// <summary>
    /// Decide che cosa fare di un nome di cartella o di file: in chiaro (sistema / parole generiche), con i nomi noti
    /// sostituiti (<c>Vendite_FULL</c> → <c>db_01_FULL</c>) oppure, se contiene qualcosa di sconosciuto, tutto
    /// anonimo (<c>dir_001</c> / <c>file_001</c>).
    /// </summary>
    private string ElaboraNome(string nome, Ruolo ruoloSeSconosciuto)
    {
        if (nome.Length == 0 || ElenchiInfrastruttura.CartelleSistema.Contains(nome)) return nome;

        var istanza = ElenchiInfrastruttura.CartellaIstanzaSql().Match(nome);
        if (istanza.Success)
        {
            var nomeIstanza = istanza.Groups["istanza"];
            if (!nomeIstanza.Success || nomeIstanza.Value.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                return nome;
            return istanza.Groups["pre"].Value + "." + _mappa.Ottieni(Ruolo.Server, nomeIstanza.Value);
        }

        var noti = NomiNoti();
        var sb = new StringBuilder();
        var i = 0;
        while (i < nome.Length)
        {
            if (EsSeparatoreNome(nome[i])) { sb.Append(nome[i++]); continue; }

            var noto = noti.FirstOrDefault(v =>
                i + v.Originale.Length <= nome.Length
                && string.Compare(nome, i, v.Originale, 0, v.Originale.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (i + v.Originale.Length == nome.Length || EsSeparatoreNome(nome[i + v.Originale.Length])));
            if (noto is not null)
            {
                sb.Append(noto.Anonimo);
                i += noto.Originale.Length;
                continue;
            }

            var j = i;
            while (j < nome.Length && !EsSeparatoreNome(nome[j])) j++;
            var parola = nome[i..j];
            if (ContieneLettere(parola) && !ElenchiInfrastruttura.ParoleGeneriche.Contains(parola))
                return _mappa.Ottieni(ruoloSeSconosciuto, nome);
            sb.Append(parola);
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>Server, database e domini già in mappa, dal nome più lungo al più corto.</summary>
    private IReadOnlyList<VoceMappa> NomiNoti()
    {
        var voci = _mappa.Voci;
        if (voci.Count != _contoVociNoti)
        {
            _noti = voci
                .Where(v => FamigliaRuolo.Di(v.Ruolo) is "srv" or "db" or "dom")
                .OrderByDescending(v => v.Originale.Length)
                .ToList();
            _contoVociNoti = voci.Count;
        }
        return _noti;
    }

    private static bool EsSeparatore(char c) => c is '\\' or '/';
    private static bool EsSeparatoreNome(char c) => c is ' ' or '_' or '.' or '-';
    private static bool ContieneLettere(string s) => s.Any(char.IsLetter);

    // ───────────────────────────── Stringhe di connessione ─────────────────────────────

    private void TrovaStringheConnessione(string t, Piano piano)
    {
        // Serve il contesto: almeno due chiavi tipiche. Un solo "Host = 'x'" può essere un'assegnazione qualsiasi.
        if (ChiaveConnessione().Matches(t).Count < 2) return;

        foreach (Match m in CoppiaConnessione().Matches(t))
        {
            var valore = m.Groups["v"];
            var testo = valore.Value.TrimEnd();
            if (testo.Length == 0) continue;
            var chiave = Regex.Replace(m.Groups["k"].Value, @"\s+", " ");
            var eDatabase = chiave.Equals("Database", StringComparison.OrdinalIgnoreCase)
                            || chiave.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase);

            piano.Aggiungi(valore.Index, valore.Index + testo.Length, TipoInfrastruttura.StringaConnessione, () =>
                eDatabase ? ElaboraDatabaseConnessione(testo) : ElaboraServerConnessione(testo));
        }

        if (Credenziali().IsMatch(t))
            piano.Avvisi.Add("Stringa di connessione con credenziali (User ID / Password): non sono gestite da questo analizzatore, controlla a mano.");
    }

    private string ElaboraDatabaseConnessione(string valore) =>
        ElenchiInfrastruttura.DatabaseDiSistema.Contains(valore) || GiaAnonimo(valore)
            ? valore
            : _mappa.Ottieni(Ruolo.Database, valore);

    /// <summary>Nome che ha già la forma di un nome anonimo (<c>srv_01</c>…): analizzare due volte non lo cambia.</summary>
    private static bool GiaAnonimo(string nome) => NomeGiaAnonimo().IsMatch(nome);

    private string ElaboraServerConnessione(string valore)
    {
        var prefisso = PrefissoProtocollo().Match(valore);
        var resto = valore[prefisso.Length..];
        if (resto.StartsWith(@"\\", StringComparison.Ordinal)) // np:\\host\pipe\sql\query
            return prefisso.Value + AnonimizzaPercorso(resto, unc: true, conUnita: false);

        var finePorta = resto.IndexOfAny([',', ':']);
        var coda = finePorta >= 0 ? resto[finePorta..] : "";
        var hostEIstanza = finePorta >= 0 ? resto[..finePorta] : resto;

        var barra = hostEIstanza.IndexOf('\\');
        var host = barra >= 0 ? hostEIstanza[..barra] : hostEIstanza;
        var istanza = barra >= 0 ? hostEIstanza[(barra + 1)..] : "";

        var localeSemplice = host is "." or "(local)" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                             || host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase);
        var hostNuovo = localeSemplice ? host : MappaHost(host);

        var sb = new StringBuilder(prefisso.Value).Append(hostNuovo);
        if (istanza.Length > 0)
        {
            var lasciaIstanza = host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase)
                                || istanza.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                                || GiaAnonimo(istanza);
            sb.Append('\\').Append(lasciaIstanza ? istanza : _mappa.Ottieni(Ruolo.Server, istanza));
        }
        return sb.Append(coda).ToString();
    }

    // ───────────────────────────── IP ─────────────────────────────

    private void TrovaIp(string t, Piano piano)
    {
        foreach (Match m in Ipv4().Matches(t))
        {
            var parti = m.Value.Split('.');
            if (parti.Any(p => p.Length > 1 && p[0] == '0')) continue;          // 010.1.1.1: non è un IP scritto bene
            if (parti.Any(p => int.Parse(p) > 255))
            {
                if (!PrecedutoDaVersione(t, m.Index))
                    piano.Avvisi.Add($"\"{m.Value}\" sembra un indirizzo IP ma non è valido: lasciato com'è.");
                continue;
            }
            if (IpDaLasciare(parti) || PrecedutoDaVersione(t, m.Index)) continue;

            piano.Aggiungi(m.Index, m.Index + m.Length, TipoInfrastruttura.Ip,
                () => _mappa.Ottieni(Ruolo.Ip, m.Value));
        }
    }

    private static bool PrecedutoDaVersione(string t, int indice)
    {
        var prima = t.AsSpan(Math.Max(0, indice - 20), Math.Min(20, indice));
        return PrimaDiVersione().IsMatch(prima);
    }

    /// <summary>Loopback, 0.0.0.0 e maschere/broadcast (255.x.x.x) non identificano nessuno.</summary>
    private static bool IpDaLasciare(string[] parti) =>
        parti[0] is "127" or "255" || parti.All(p => p == "0");

    private static bool EIpValido(string s)
    {
        var parti = s.Split('.');
        return parti.Length == 4 && parti.All(p => p.Length is >= 1 and <= 3 && p.All(char.IsAsciiDigit)
                                                   && (p.Length == 1 || p[0] != '0') && int.Parse(p) <= 255);
    }

    // ───────────────────────────── Host / FQDN ─────────────────────────────

    private void TrovaHost(string t, Piano piano)
    {
        foreach (Match m in HostNelTesto().Matches(t))
        {
            var host = m.Groups["h"].Value;
            if (!ElenchiInfrastruttura.TldTestoLibero.Contains(m.Groups["tld"].Value)) continue;
            var etichette = host.Split('.');
            if (etichette.Any(e => ElenchiInfrastruttura.EtichetteSql.Contains(e))) continue;
            if (etichette.All(e => e.All(char.IsAsciiDigit))) continue;

            piano.Aggiungi(m.Groups["h"].Index, m.Groups["h"].Index + host.Length, TipoInfrastruttura.Host,
                () => MappaHost(host));
        }
    }

    /// <summary>
    /// Anonimizza un host: IP → <c>ip_NNN</c>; nome semplice → <c>srv_NN</c>; FQDN → il primo pezzo è il server, i
    /// pezzi centrali sono domini (<c>dom_NN</c>, gli stessi dei domini AD) e il suffisso noto (<c>.com</c>,
    /// <c>.local</c>) resta. <c>localhost</c> e <c>www</c> restano.
    /// </summary>
    private string MappaHost(string host)
    {
        if (host.Length == 0 || GiaAnonimo(host)) return host;
        if (EIpValido(host))
            return IpDaLasciare(host.Split('.')) ? host : _mappa.Ottieni(Ruolo.Ip, host);
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return host;

        var parti = host.Split('.');
        if (parti.Any(p => p.Length == 0)) return host;
        if (parti.Length == 1) return _mappa.Ottieni(Ruolo.Server, host);

        var conSuffisso = ElenchiInfrastruttura.TldContesto.Contains(parti[^1]);
        var daMappare = conSuffisso ? parti.Length - 1 : parti.Length;

        var risultato = (string[])parti.Clone();
        for (var k = 0; k < daMappare; k++)
        {
            if (GiaAnonimo(parti[k])) continue;
            if (k == 0 && parti[0].Equals("www", StringComparison.OrdinalIgnoreCase) && daMappare > 1) continue;
            var eServer = k == 0 && !(daMappare == 1 && conSuffisso);
            risultato[k] = _mappa.Ottieni(eServer ? Ruolo.Server : Ruolo.Dominio, parti[k]);
        }
        return string.Join('.', risultato);
    }

    // ───────────────────────────── Supporto ─────────────────────────────

    private sealed record Pezzo(int Inizio, int Fine, TipoInfrastruttura Tipo, string Nuovo);

    private sealed class Piano
    {
        public List<Pezzo> Pezzi { get; } = [];
        public List<string> Avvisi { get; } = [];

        public void Aggiungi(int inizio, int fine, TipoInfrastruttura tipo, Func<string> calcola)
        {
            if (Pezzi.Any(p => inizio < p.Fine && p.Inizio < fine)) return; // già preso da un rilevatore precedente
            Pezzi.Add(new Pezzo(inizio, fine, tipo, calcola()));
        }
    }

    [GeneratedRegex(@"\b(?<schema>https?|ftps?|sftp|wss?|ssh|smb)://(?:(?<utente>[^\s/@:'""<>]+)(?::(?<pwd>[^\s/@'""<>]*))?@)?(?<host>[A-Za-z0-9._\-]+)(?::(?<porta>\d+))?(?<resto>[/?#][^\s'""<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"(?<![\w.%+\-])(?<l>[A-Za-z0-9][A-Za-z0-9._%+\-]*)@(?<d>(?:[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,})(?![\w@])")]
    private static partial Regex Email();

    // \\host\...  oppure  //host/...  (non dopo "http:", non \\. e \\? che sono percorsi di dispositivo)
    [GeneratedRegex(@"(?<![\\/\w:.<*])(?:\\\\|//)(?![\\/.?])(?=[\w.$\-]+(?:[\\/]|\s|$|[""'<>|,;)]))")]
    private static partial Regex InizioUnc();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z]:[\\/](?=[^\\/\s])")]
    private static partial Regex InizioLocale();

    [GeneratedRegex(@"(?<![\w:/\\.<*])/(?=(?:var|opt|home|mnt|usr|tmp|srv|media|etc|root|data|backup|backups)/)", RegexOptions.IgnoreCase)]
    private static partial Regex InizioUnix();

    [GeneratedRegex(@"^\s+(?:\\\\|[A-Za-z]:[\\/])")]
    private static partial Regex InizioAltroPercorso();

    [GeneratedRegex(@"\.(?<e>[A-Za-z0-9]{1,8})(?=\s)")]
    private static partial Regex EstensioneSeguitaDaSpazio();

    [GeneratedRegex(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?!\w|\.\d)")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"(?:\bv|\bver\.?\s*|\bversion\w*\s*|\bversione\s*|\bbuild\s*|\brelease\s*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PrimaDiVersione();

    [GeneratedRegex(@"(?<![\w.\\/@\-])(?<h>(?:[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?\.)+(?<tld>[A-Za-z]{2,}))(?![\w\-@]|\.\w)")]
    private static partial Regex HostNelTesto();

    [GeneratedRegex(@"(?<![\w@])(?:Data\s+Source|Server|Address|Addr|Network\s+Address|Host|Database|Initial\s+Catalog|User\s*ID|UID|Password|Pwd|Trusted_Connection|Integrated\s+Security|Provider|Driver)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ChiaveConnessione();

    [GeneratedRegex(@"(?<![\w@])(?<k>Data\s+Source|Server|Address|Addr|Network\s+Address|Host|Database|Initial\s+Catalog)\s*=\s*(?<v>[^;'""\r\n]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CoppiaConnessione();

    [GeneratedRegex(@"(?<![\w@])(?:User\s*ID|UID|Password|Pwd)\s*=\s*[^;'""\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex Credenziali();

    [GeneratedRegex(@"^(?:tcp|np|lpc):", RegexOptions.IgnoreCase)]
    private static partial Regex PrefissoProtocollo();

    [GeneratedRegex(@"^(?:srv|db|dom|dir|file|ip)_\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex NomeGiaAnonimo();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:mail_\d{3,}@example\.invalid|(?:srv|db|dom)_\d{2,}|(?:dir|file|ip)_\d{3,})(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex NomeAnonimo();
}
