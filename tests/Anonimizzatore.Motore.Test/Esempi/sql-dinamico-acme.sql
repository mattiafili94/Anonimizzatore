-- Esempio INVENTATO per i test: nomi, server e percorsi sono di fantasia.
CREATE PROCEDURE dbo.uspChiudiOrdini
    @CodiceAcme varchar(20),
    @DataLimite date
AS
BEGIN
    DECLARE @sql nvarchar(max), @tabella sysname = N'Ordini', @conta int;

    -- 1) SQL dinamico completo e valido
    EXEC (N'UPDATE dbo.Ordini SET Stato = ''Chiuso'' WHERE CodiceAcme = ''A1'' AND DataOrdine < ''20260101''');

    -- 2) sp_executesql con parametri
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.Ordini AS o WHERE o.CodiceAcme = @c AND o.DataOrdine < @d /* solo ordini vecchi */';
    EXEC sp_executesql @sql, N'@n int OUTPUT, @c varchar(20), @d date', @n = @conta OUTPUT, @c = @CodiceAcme, @d = @DataLimite;

    -- 3) SQL dinamico dentro SQL dinamico
    EXEC (N'EXEC (''DELETE FROM dbo.OrdiniArchivio WHERE CodiceAcme = ''''A1'''''')');

    -- 4) SQL costruito a pezzi
    SET @sql = N'SELECT * FROM dbo.' + QUOTENAME(@tabella) + N' WHERE CodiceAcme = ''' + @CodiceAcme + N'''';
    EXEC (@sql);
    SET @sql = N'SELECT * FROM ' + @tabella + N' ORDER BY DataOrdine';
    EXEC (@sql);
END;
GO

-- 5) Job di SQL Agent
EXEC msdb.dbo.sp_add_job @job_name = N'Chiusura ordini notturna', @enabled = 1, @owner_login_name = N'ACMEDOM\svc_agent';
EXEC msdb.dbo.sp_add_jobstep @job_name = N'Chiusura ordini notturna', @step_name = N'Chiudi',
    @subsystem = N'TSQL', @database_name = N'Vendite',
    @command = N'EXEC dbo.uspChiudiOrdini @CodiceAcme = ''A1'', @DataLimite = ''20260101'';';
EXEC msdb.dbo.sp_add_jobstep @job_name = N'Chiusura ordini notturna', @step_name = N'Copia',
    @subsystem = N'CmdExec', @command = N'robocopy D:\Export \\SRVBACKUP\Condivisa\Export /E';
EXEC msdb.dbo.sp_add_jobserver @job_name = N'Chiusura ordini notturna', @server_name = N'(local)';
EXEC msdb.dbo.sp_start_job @job_name = N'Chiusura ordini notturna';
