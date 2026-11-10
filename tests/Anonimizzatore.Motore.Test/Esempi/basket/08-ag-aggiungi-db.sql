-- Aggiungere il database VenditeZeta al gruppo di disponibilita' AGZeta
:connect SRVSQL01
BACKUP DATABASE [VenditeZeta] TO DISK = N'\\SRVBACKUP01\AG\VenditeZeta.bak' WITH COPY_ONLY, COMPRESSION, FORMAT;
BACKUP LOG [VenditeZeta] TO DISK = N'\\SRVBACKUP01\AG\VenditeZeta.trn' WITH COMPRESSION;
GO
:connect SRVSQL02
RESTORE DATABASE [VenditeZeta] FROM DISK = N'\\SRVBACKUP01\AG\VenditeZeta.bak' WITH NORECOVERY;
RESTORE LOG [VenditeZeta] FROM DISK = N'\\SRVBACKUP01\AG\VenditeZeta.trn' WITH NORECOVERY;
GO
:connect SRVSQL01
ALTER AVAILABILITY GROUP [AGZeta] ADD DATABASE [VenditeZeta];
GO
:connect SRVSQL02
ALTER DATABASE [VenditeZeta] SET HADR AVAILABILITY GROUP = [AGZeta];
GO
SELECT d.name, drs.synchronization_state_desc, drs.database_state_desc
FROM sys.dm_hadr_database_replica_states drs
JOIN sys.databases d ON d.database_id = drs.database_id
WHERE d.name = N'VenditeZeta';
