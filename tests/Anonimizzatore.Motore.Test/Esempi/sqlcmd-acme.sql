-- Esempio INVENTATO per i test (modalità SQLCMD): server, utenti, percorsi e password sono di fantasia.
:setvar DbName Vendite
:setvar ServerRemoto SRVREMOTO
:setvar CartellaDati D:\Dati\Acme
:setvar PasswordApp Segr3ta!
:setvar Versione 3
:on error exit
:connect SRVSQL01 -U ACMEDOM\svc_deploy -P Pwd123!
:r D:\Script\Acme\schema.sql
:out $(CartellaDati)\log.txt

USE [$(DbName)];
GO

CREATE TABLE dbo.Ordini (Id int, CodiceAcme varchar(20));
GO

SELECT * FROM [$(ServerRemoto)].[$(DbName)].dbo.Ordini;
!!sqlcmd -S SRVSQL01 -d Vendite -U sa -P Segr3taSa -i D:\Script\Acme\controllo.sql
GO 5
