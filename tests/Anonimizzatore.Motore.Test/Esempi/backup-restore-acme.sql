-- Esempio INVENTATO per i test: server, percorsi, utenti e password sono di fantasia.
BACKUP DATABASE Vendite
    TO DISK = N'\\SRVBACKUP\Condivisa\Vendite_FULL_20260101.bak'
    WITH INIT, COMPRESSION, NAME = N'Vendite full', STATS = 10;
GO

BACKUP LOG [Vendite] TO DISK = 'D:\Backup\Vendite_LOG.trn';
GO

RESTORE DATABASE Vendite_Copia
    FROM DISK = N'\\SRVBACKUP\Condivisa\Vendite_FULL_20260101.bak'
    WITH MOVE N'Vendite_Data' TO N'D:\Dati\Vendite_Copia.mdf',
         MOVE N'Vendite_Log'  TO N'E:\Log\Vendite_Copia.ldf',
         REPLACE, STATS = 5;
GO

ALTER DATABASE Vendite_Copia SET RECOVERY SIMPLE;
ALTER DATABASE Vendite_Copia MODIFY NAME = Vendite_Test;
DBCC CHECKDB (N'Vendite_Test') WITH NO_INFOMSGS;
DBCC SHRINKFILE (N'Vendite_Log', 1);
DROP DATABASE IF EXISTS Vendite_Test;
GO

EXEC sp_addlinkedserver @server = N'SRVREMOTO', @srvproduct = N'', @provider = N'SQLNCLI',
     @datasrc = N'SRVSQL02\ISTANZA1', @catalog = N'Contabilita';
EXEC sp_addlinkedsrvlogin 'SRVREMOTO', 'false', NULL, 'utente_remoto', 'Segr3ta';
GO

DECLARE @conn nvarchar(400) = N'Server=SRVSQL02\ISTANZA1;Database=Contabilita;User ID=ACMEDOM\svc_app;Password=Pwd123!;';
DECLARE @destinatario varchar(200) = 'dba@acme.example';
DECLARE @url varchar(200) = 'https://SRVSQL02.acme.example/report/vendite?anno=2026';
EXEC xp_cmdshell 'copy D:\Backup\Vendite_LOG.trn \\10.0.0.5\archivio$\';
SELECT * FROM OPENQUERY(SRVREMOTO, 'SELECT 1');
