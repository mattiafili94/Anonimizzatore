EXEC master.dbo.sp_addlinkedserver @server = N'SRVMAGAZZINO', @srvproduct = N'', @provider = N'SQLNCLI', @datasrc = N'srvmag01.zeta.local\ISTMAG';
EXEC master.dbo.sp_addlinkedsrvlogin @rmtsrvname = N'SRVMAGAZZINO', @useself = N'False', @rmtuser = N'lettore_mag', @rmtpassword = N'Segreta#2026';
EXEC master.dbo.sp_serveroption @server = N'SRVMAGAZZINO', @optname = N'rpc out', @optvalue = N'true';
GO
SELECT a.CodiceArticolo, a.Giacenza FROM [SRVMAGAZZINO].[MagazzinoZeta].[dbo].[Articoli] a WHERE a.Giacenza < 5;
SELECT * FROM OPENQUERY(SRVMAGAZZINO, 'SELECT CodiceArticolo, Giacenza FROM MagazzinoZeta.dbo.Articoli WHERE Giacenza < 5');
EXEC SRVMAGAZZINO.MagazzinoZeta.dbo.usp_AggiornaGiacenze @Data = '20261110';
GO
EXEC master.dbo.sp_dropserver @server = N'SRVMAGAZZINO', @droplogins = 'droplogins';
GO
