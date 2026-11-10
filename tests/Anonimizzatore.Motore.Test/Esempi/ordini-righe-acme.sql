-- Esempio INVENTATO per i test: "Acme" è un cliente di fantasia, nessun dato reale.
CREATE TABLE dbo.Ordini
(
    IdOrdine      int           NOT NULL CONSTRAINT PK_Ordini PRIMARY KEY,
    CodiceAcme    varchar(20)   NOT NULL,
    Importo       decimal(18,2) NOT NULL CONSTRAINT DF_Ordini_Importo DEFAULT (0),
    Note          nvarchar(max) NULL
);
GO

CREATE TABLE dbo.Righe
(
    IdRiga        int           NOT NULL,
    IdOrdine      int           NOT NULL,
    Quantita      int           NOT NULL,
    CONSTRAINT PK_Righe PRIMARY KEY (IdRiga),
    CONSTRAINT FK_Righe_Ordini FOREIGN KEY (IdOrdine) REFERENCES dbo.Ordini (IdOrdine),
    CONSTRAINT CK_Righe_Quantita CHECK (Quantita > 0)
);
GO

CREATE UNIQUE INDEX UX_Ordini_CodiceAcme ON dbo.Ordini (CodiceAcme);
CREATE INDEX IX_Righe_Ordini ON dbo.Righe (IdOrdine) INCLUDE (Quantita);
GO

CREATE PROCEDURE dbo.uspTotaleOrdini
    @CodiceAcme varchar(20),
    @Minimo     decimal(18,2) = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TotaleAcme decimal(18,2), @Conteggio int;

    WITH AcmeRecenti AS
    (
        SELECT o.IdOrdine, o.Importo
        FROM   dbo.Ordini AS o
        WHERE  o.CodiceAcme = @CodiceAcme AND o.Importo >= @Minimo
    )
    SELECT @TotaleAcme = SUM(r.Importo), @Conteggio = COUNT(*)
    FROM   AcmeRecenti AS r;

    SELECT acme.IdOrdine, SUM(rg.Quantita) AS QuantitaAcme
    FROM   dbo.Ordini AS acme
    JOIN   dbo.Righe  AS rg ON rg.IdOrdine = acme.IdOrdine
    GROUP  BY acme.IdOrdine
    ORDER  BY QuantitaAcme DESC;

    SELECT @TotaleAcme AS Totale, @Conteggio AS NumeroRighe, @@ROWCOUNT AS Toccate;
END;
GO

EXEC dbo.uspTotaleOrdini @CodiceAcme = 'A1', @Minimo = 10;
EXEC msdb.dbo.sp_add_job @job_name = N'Notturno';
