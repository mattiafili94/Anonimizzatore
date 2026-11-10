USE msdb;
GO
EXEC dbo.sp_add_job @job_name = N'BackupLogVenditeZeta', @enabled = 1, @owner_login_name = N'ZETA\svc_sql', @description = N'Backup log ogni 15 minuti';
EXEC dbo.sp_add_jobstep @job_name = N'BackupLogVenditeZeta', @step_name = N'Backup', @subsystem = N'TSQL', @database_name = N'VenditeZeta',
     @command = N'BACKUP LOG [VenditeZeta] TO DISK = N''D:\Backup\VenditeZeta_LOG.trn'' WITH COMPRESSION;';
EXEC dbo.sp_add_jobstep @job_name = N'BackupLogVenditeZeta', @step_name = N'Copia', @subsystem = N'CmdExec',
     @command = N'robocopy D:\Backup \\SRVBACKUP01\Backup\SQL /MIR';
EXEC dbo.sp_add_schedule @schedule_name = N'Ogni15Minuti', @freq_type = 4, @freq_interval = 1, @freq_subday_type = 4, @freq_subday_interval = 15;
EXEC dbo.sp_attach_schedule @job_name = N'BackupLogVenditeZeta', @schedule_name = N'Ogni15Minuti';
EXEC dbo.sp_add_jobserver @job_name = N'BackupLogVenditeZeta', @server_name = N'(local)';
EXEC dbo.sp_start_job @job_name = N'BackupLogVenditeZeta';
GO
SELECT j.name, h.run_status, h.run_date FROM dbo.sysjobs j JOIN dbo.sysjobhistory h ON h.job_id = j.job_id WHERE j.name LIKE N'%Zeta%';
GO
