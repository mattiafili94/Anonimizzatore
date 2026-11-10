RESTORE FILELISTONLY FROM DISK = N'D:\Backup\VenditeZeta_FULL.bak';
RESTORE HEADERONLY FROM DISK = N'D:\Backup\VenditeZeta_FULL.bak';
GO
RESTORE DATABASE [VenditeZeta_Test] FROM DISK = N'D:\Backup\VenditeZeta_FULL.bak'
WITH MOVE N'VenditeZeta' TO N'E:\Data\VenditeZeta_Test.mdf',
     MOVE N'VenditeZeta_log' TO N'F:\Log\VenditeZeta_Test_log.ldf',
     NORECOVERY, REPLACE, STATS = 5;
GO
RESTORE DATABASE [VenditeZeta_Test] FROM DISK = N'D:\Backup\VenditeZeta_DIFF.bak' WITH NORECOVERY;
RESTORE LOG [VenditeZeta_Test] FROM DISK = N'D:\Backup\VenditeZeta_LOG.trn'
WITH STANDBY = N'D:\Backup\VenditeZeta_Test_undo.tuf', STOPAT = N'2026-11-10T08:30:00';
GO
RESTORE DATABASE [VenditeZeta_Test] WITH RECOVERY;
GO
ALTER DATABASE [VenditeZeta_Test] SET MULTI_USER;
ALTER DATABASE [VenditeZeta_Test] MODIFY NAME = [VenditeZeta_Copia];
DBCC CHECKDB (N'VenditeZeta_Copia') WITH NO_INFOMSGS;
GO
