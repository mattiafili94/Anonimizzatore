CREATE VIEW dbo.vw_OrdiniAperti WITH SCHEMABINDING AS
SELECT o.IdOrdine, o.IdCliente, o.Importo FROM dbo.Ordini AS o WHERE o.Stato = 'A';
GO
CREATE FUNCTION dbo.fn_TotaleCliente (@IdCliente int)
RETURNS TABLE
AS RETURN (SELECT SUM(Importo) AS Totale FROM dbo.Ordini WHERE IdCliente = @IdCliente);
GO
CREATE TRIGGER dbo.trg_Ordini_Audit ON dbo.Ordini AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.AuditOrdini (IdOrdine, Azione, Utente, Quando)
    SELECT COALESCE(i.IdOrdine, d.IdOrdine),
           CASE WHEN i.IdOrdine IS NULL THEN 'D' WHEN d.IdOrdine IS NULL THEN 'I' ELSE 'U' END,
           SUSER_SNAME(), SYSDATETIME()
    FROM inserted i FULL JOIN deleted d ON d.IdOrdine = i.IdOrdine;
END
GO
SELECT c.RagioneSociale, t.Totale FROM dbo.Clienti c CROSS APPLY dbo.fn_TotaleCliente(c.IdCliente) t;
GO
