CREATE OR ALTER PROCEDURE dbo.usp_ChiudiOrdiniScaduti
    @GiorniScadenza int = 30,
    @Operatore nvarchar(128),
    @OrdiniChiusi int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql nvarchar(max), @tabella sysname = N'dbo.Ordini';
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE o SET o.Stato = 'C', o.ChiusoDa = @Operatore, o.DataChiusura = GETDATE()
        FROM dbo.Ordini o
        WHERE o.Stato = 'A' AND DATEDIFF(DAY, o.DataOrdine, GETDATE()) > @GiorniScadenza;
        SET @OrdiniChiusi = @@ROWCOUNT;

        SET @sql = N'INSERT INTO dbo.LogOperazioni (Tabella, Operatore, Righe) SELECT @t, @op, @n';
        EXEC sp_executesql @sql, N'@t sysname, @op nvarchar(128), @n int', @t = @tabella, @op = @Operatore, @n = @OrdiniChiusi;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @msg nvarchar(2048) = ERROR_MESSAGE();
        RAISERROR(@msg, 16, 1);
    END CATCH
END
GO
DECLARE @n int;
EXEC dbo.usp_ChiudiOrdiniScaduti @GiorniScadenza = 60, @Operatore = N'ZETA\mario.rossi', @OrdiniChiusi = @n OUTPUT;
SELECT @n AS Chiusi;
GO
