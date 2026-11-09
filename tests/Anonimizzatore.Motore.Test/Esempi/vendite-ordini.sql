-- Esempio INVENTATO per i test: nessun dato reale.
USE [Vendite];
GO

CREATE TABLE [Vendite].[Ordini]
(
    IdOrdine     int           NOT NULL CONSTRAINT PK_Ordini PRIMARY KEY,
    IdCliente    int           NOT NULL,
    DataOrdine   datetime2(0)  NOT NULL,
    Importo      decimal(18,2) NOT NULL
);
GO

/* Restituisce gli ordini di un cliente
   in un intervallo di date */
CREATE PROCEDURE [Vendite].[usp_OrdiniCliente]
    @IdCliente int,
    @DataDa    date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.IdOrdine, o.DataOrdine, o.Importo
    FROM   [Vendite].[Ordini] AS o
    WHERE  o.IdCliente = @IdCliente
      AND  o.DataOrdine >= @DataDa;   -- filtro sulla data
END;
GO
