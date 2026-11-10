CREATE TABLE dbo.Ordini (
    IdOrdine int IDENTITY(1,1) NOT NULL,
    IdCliente int NOT NULL,
    DataOrdine datetime2(0) NOT NULL CONSTRAINT DF_Ordini_Data DEFAULT (SYSDATETIME()),
    Stato char(1) NOT NULL,
    Importo decimal(18,2) NULL,
    CONSTRAINT PK_Ordini PRIMARY KEY CLUSTERED (IdOrdine),
    CONSTRAINT CK_Ordini_Stato CHECK (Stato IN ('A','C'))
);
GO
ALTER TABLE dbo.Clienti ADD CONSTRAINT PK_Clienti PRIMARY KEY (IdCliente);
ALTER TABLE dbo.Ordini ADD CONSTRAINT FK_Ordini_Clienti FOREIGN KEY (IdCliente) REFERENCES dbo.Clienti (IdCliente) ON DELETE CASCADE;
ALTER TABLE dbo.Clienti ADD CONSTRAINT UQ_Clienti_PartitaIva UNIQUE (PartitaIva);
ALTER TABLE dbo.Ordini NOCHECK CONSTRAINT FK_Ordini_Clienti;
ALTER TABLE dbo.Ordini WITH CHECK CHECK CONSTRAINT FK_Ordini_Clienti;
GO
CREATE NONCLUSTERED INDEX IX_Ordini_Cliente_Data ON dbo.Ordini (IdCliente, DataOrdine DESC) INCLUDE (Importo) WHERE Stato = 'A';
CREATE UNIQUE INDEX UX_Clienti_RagioneSociale ON dbo.Clienti (RagioneSociale);
ALTER INDEX IX_Ordini_Cliente_Data ON dbo.Ordini REBUILD WITH (ONLINE = ON, FILLFACTOR = 90);
DROP INDEX IX_Ordini_Cliente_Data ON dbo.Ordini;
ALTER TABLE dbo.Ordini DROP CONSTRAINT FK_Ordini_Clienti;
ALTER TABLE dbo.Ordini DROP CONSTRAINT PK_Ordini;
EXEC sp_rename 'dbo.Ordini.Importo', 'ImportoTotale', 'COLUMN';
GO
