-- Esempio INVENTATO per i test: "ACMEDOM" e "Vendite" sono nomi di fantasia, nessun dato reale.
USE [Vendite];
GO

CREATE LOGIN [ACMEDOM\mario.rossi] FROM WINDOWS WITH DEFAULT_DATABASE = [Vendite];
CREATE LOGIN svc_vendite WITH PASSWORD = N'S3gr3ta!Lunga', SID = 0x1A2B3C4D5E6F708192A3B4C5D6E7F801,
    DEFAULT_DATABASE = [Vendite], CHECK_POLICY = OFF;
GO

CREATE USER [ACMEDOM\mario.rossi] FOR LOGIN [ACMEDOM\mario.rossi] WITH DEFAULT_SCHEMA = [Vendite];
CREATE USER svc_vendite FOR LOGIN svc_vendite;
CREATE ROLE ruolo_contabilita AUTHORIZATION dbo;
ALTER ROLE ruolo_contabilita ADD MEMBER [ACMEDOM\mario.rossi];
ALTER ROLE db_datareader ADD MEMBER svc_vendite;
ALTER SERVER ROLE sysadmin ADD MEMBER [NT AUTHORITY\SYSTEM];
GO

GRANT SELECT, UPDATE ON OBJECT::Vendite.Ordini TO ruolo_contabilita;
GRANT EXECUTE ON Vendite.usp_OrdiniCliente TO svc_vendite WITH GRANT OPTION;
GRANT SELECT ON SCHEMA::Vendite TO ruolo_contabilita;
GRANT SELECT (IdOrdine, Importo) ON Vendite.Ordini TO public;
DENY DELETE ON Vendite.Ordini TO [ACMEDOM\mario.rossi];
REVOKE SELECT ON Vendite.Ordini FROM [ACMEDOM\mario.rossi];
GO

EXEC sp_addrolemember N'ruolo_contabilita', N'svc_vendite';
EXECUTE AS USER = N'svc_vendite';
REVERT;
ALTER LOGIN svc_vendite WITH PASSWORD = 'nuova-password' OLD_PASSWORD = 'vecchia-password';
ALTER AUTHORIZATION ON SCHEMA::Vendite TO [ACMEDOM\mario.rossi];
GO

DROP USER svc_vendite;
DROP LOGIN svc_vendite;
DROP ROLE ruolo_contabilita;
