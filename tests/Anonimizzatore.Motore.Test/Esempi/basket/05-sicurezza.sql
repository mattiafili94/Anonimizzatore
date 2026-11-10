USE [master];
CREATE LOGIN [ZETA\mario.rossi] FROM WINDOWS WITH DEFAULT_DATABASE = [VenditeZeta];
CREATE LOGIN app_vendite WITH PASSWORD = N'Pa$$w0rd!Segreta1', CHECK_POLICY = ON, DEFAULT_DATABASE = [VenditeZeta];
ALTER SERVER ROLE [sysadmin] ADD MEMBER [ZETA\mario.rossi];
GO
USE [VenditeZeta];
CREATE USER [ZETA\mario.rossi] FOR LOGIN [ZETA\mario.rossi] WITH DEFAULT_SCHEMA = [vendite];
CREATE USER app_vendite FOR LOGIN app_vendite;
CREATE ROLE ruolo_report AUTHORIZATION dbo;
ALTER ROLE ruolo_report ADD MEMBER app_vendite;
ALTER ROLE db_datareader ADD MEMBER [ZETA\mario.rossi];
GO
GRANT SELECT ON dbo.Ordini TO ruolo_report;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::vendite TO app_vendite WITH GRANT OPTION;
GRANT EXECUTE ON dbo.usp_ChiudiOrdiniScaduti TO app_vendite;
GRANT SELECT ON dbo.Clienti (IdCliente, RagioneSociale) TO ruolo_report;
DENY DELETE ON dbo.Ordini TO app_vendite;
DENY VIEW DEFINITION ON OBJECT::dbo.Clienti TO [ZETA\mario.rossi];
REVOKE INSERT ON SCHEMA::vendite FROM app_vendite CASCADE;
GRANT VIEW SERVER STATE TO [ZETA\mario.rossi];
GRANT CONNECT SQL TO app_vendite;
EXECUTE AS USER = 'app_vendite';
SELECT USER_NAME();
REVERT;
DROP USER app_vendite;
DROP LOGIN app_vendite;
GO
