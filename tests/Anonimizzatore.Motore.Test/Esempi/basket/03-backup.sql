-- Backup completo, differenziale e log
BACKUP DATABASE [VenditeZeta] TO DISK = N'\\SRVBACKUP01\Backup\SQL\VenditeZeta_FULL.bak'
WITH COMPRESSION, CHECKSUM, INIT, FORMAT, NAME = N'VenditeZeta-Full', STATS = 10;
GO
BACKUP DATABASE [VenditeZeta] TO DISK = N'D:\Backup\VenditeZeta_DIFF.bak' WITH DIFFERENTIAL, COMPRESSION, CHECKSUM;
GO
BACKUP LOG [VenditeZeta] TO DISK = N'D:\Backup\VenditeZeta_LOG.trn' WITH COMPRESSION, CHECKSUM;
GO
BACKUP DATABASE [VenditeZeta] TO
    DISK = N'E:\Backup\VenditeZeta_1.bak',
    DISK = N'F:\Backup\VenditeZeta_2.bak'
WITH COPY_ONLY, COMPRESSION;
GO
BACKUP DATABASE [VenditeZeta] TO URL = N'https://zetastorage.blob.core.windows.net/backup/VenditeZeta.bak' WITH CREDENTIAL = N'CredenzialeZeta';
GO
RESTORE VERIFYONLY FROM DISK = N'D:\Backup\VenditeZeta_FULL.bak' WITH CHECKSUM;
GO
