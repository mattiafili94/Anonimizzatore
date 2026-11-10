-- Report vendite mensili per cliente Zeta Spa
USE [VenditeZeta];
GO
WITH VenditeMese AS (
    SELECT c.IdCliente, c.RagioneSociale, YEAR(o.DataOrdine) AS Anno, MONTH(o.DataOrdine) AS Mese,
           SUM(r.Quantita * r.PrezzoUnitario) AS Totale
    FROM dbo.Clienti AS c
    INNER JOIN dbo.Ordini AS o ON o.IdCliente = c.IdCliente
    INNER JOIN dbo.RigheOrdine r ON r.IdOrdine = o.IdOrdine
    WHERE o.DataOrdine >= '20260101' /* solo anno corrente */
    GROUP BY c.IdCliente, c.RagioneSociale, YEAR(o.DataOrdine), MONTH(o.DataOrdine)
)
SELECT v.*, RANK() OVER (PARTITION BY v.Anno, v.Mese ORDER BY v.Totale DESC) AS Posizione
INTO #Classifica
FROM VenditeMese v;

MERGE dbo.StatisticheCliente AS dest
USING #Classifica AS src ON dest.IdCliente = src.IdCliente AND dest.Anno = src.Anno AND dest.Mese = src.Mese
WHEN MATCHED THEN UPDATE SET dest.Totale = src.Totale, dest.Posizione = src.Posizione
WHEN NOT MATCHED BY TARGET THEN INSERT (IdCliente, Anno, Mese, Totale, Posizione) VALUES (src.IdCliente, src.Anno, src.Mese, src.Totale, src.Posizione)
WHEN NOT MATCHED BY SOURCE THEN DELETE;

SELECT TOP (10) * FROM #Classifica ORDER BY Totale DESC;
DROP TABLE #Classifica;
GO
