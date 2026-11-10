INSERT INTO dbo.Clienti (RagioneSociale, PartitaIva, Email) VALUES (N'Zeta Spa', '01234567890', 'amministrazione@zeta.example'), (N'Bianchi Srl', '09876543210', NULL);
GO
INSERT INTO dbo.StoricoOrdini (IdOrdine, IdCliente, DataOrdine, Importo)
OUTPUT inserted.IdOrdine, inserted.Importo INTO #Inseriti (IdOrdine, Importo)
SELECT o.IdOrdine, o.IdCliente, o.DataOrdine, o.Importo
FROM dbo.Ordini o
WHERE o.DataOrdine < DATEADD(YEAR, -2, GETDATE());

DELETE TOP (5000) FROM dbo.Ordini WHERE DataOrdine < DATEADD(YEAR, -2, GETDATE());
UPDATE c SET c.Email = LOWER(c.Email) FROM dbo.Clienti c WHERE c.Email IS NOT NULL;
TRUNCATE TABLE dbo.LogOperazioni;
GO
BULK INSERT dbo.ImportClienti FROM 'D:\Import\clienti_zeta.csv' WITH (FIELDTERMINATOR = ';', ROWTERMINATOR = '\n', FIRSTROW = 2);
SELECT * INTO dbo.ClientiBackup FROM dbo.Clienti;
GO
