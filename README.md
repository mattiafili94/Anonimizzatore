# Anonimizzatore T-SQL

App desktop per Windows che anonimizza script T-SQL di SQL Server (query, stored procedure, DDL, permessi,
backup/restore, job) prima di passarli a un'IA, e rimette i nomi originali nelle risposte.

- Funziona **completamente offline**: nessuna chiamata di rete.
- Un **profilo per cliente**, con la sua mappa dei nomi salvata in locale e cifrata.
- Tutto con il **copia-incolla**: incolli gli script, copi il testo anonimizzato; incolli la risposta dell'IA, copi
  il risultato con i nomi veri.

Stato: in progettazione. Decisioni e piano di lavoro sono in [CLAUDE.md](CLAUDE.md).

## Licenza

[MIT](LICENSE)
