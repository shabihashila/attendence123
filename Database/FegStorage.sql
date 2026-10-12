:on error exit
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FileOperationLog')
BEGIN
    CREATE TABLE dbo.FileOperationLog (
        Id         BIGINT IDENTITY(1,1) PRIMARY KEY,
        Kind       NVARCHAR(20) NOT NULL,
        Provider   NVARCHAR(20) NOT NULL,
        Container  NVARCHAR(120) NOT NULL,
        [Key]      NVARCHAR(500) NOT NULL,
        SizeBytes  BIGINT NULL,
        Status     NVARCHAR(10) NOT NULL,
        Error      NVARCHAR(2000) NULL,
        CallerKey  NVARCHAR(120) NULL,
        AtTime     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT CK_FileOpLog_Kind     CHECK (Kind     IN (N'Upload', N'Download', N'Delete', N'ViewUrl')),
        CONSTRAINT CK_FileOpLog_Status   CHECK (Status   IN (N'Ok', N'Failed'))
    );
    CREATE INDEX IX_FileOpLog_Kind_AtTime ON dbo.FileOperationLog(Kind, AtTime DESC);
    CREATE INDEX IX_FileOpLog_Container_AtTime ON dbo.FileOperationLog(Container, AtTime DESC);
END
GO
IF SCHEMA_ID(N'logs') IS NULL EXEC(N'CREATE SCHEMA logs');
IF OBJECT_ID(N'logs.Logs',N'U') IS NULL CREATE TABLE logs.Logs(Id INT IDENTITY PRIMARY KEY,Message NVARCHAR(MAX),Level NVARCHAR(128),TimeStamp DATETIME,Exception NVARCHAR(MAX));
GO
