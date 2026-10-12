:on error exit
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CommunicationLog')
BEGIN
    CREATE TABLE dbo.CommunicationLog (
        Id                BIGINT IDENTITY(1,1) PRIMARY KEY,
        Kind              NVARCHAR(10) NOT NULL,
        Recipient         NVARCHAR(300) NOT NULL,
        Subject           NVARCHAR(300) NULL,
        Sender            NVARCHAR(120) NULL,
        Status            NVARCHAR(10) NOT NULL,
        Error             NVARCHAR(2000) NULL,
        ProviderMessageId NVARCHAR(120) NULL,
        Provider          NVARCHAR(40)  NULL,
        CallerKey         NVARCHAR(120) NULL,
        SentAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT CK_CommLog_Kind   CHECK (Kind   IN (N'Email', N'Sms')),
        CONSTRAINT CK_CommLog_Status CHECK (Status IN (N'Sent',  N'Failed'))
    );
    CREATE INDEX IX_CommLog_Kind_SentAt ON dbo.CommunicationLog(Kind, SentAt DESC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.CommunicationLog') AND name = 'Provider')
BEGIN
    ALTER TABLE dbo.CommunicationLog ADD Provider NVARCHAR(40) NULL;
END
GO
IF SCHEMA_ID(N'logs') IS NULL EXEC(N'CREATE SCHEMA logs');
IF OBJECT_ID(N'logs.Logs',N'U') IS NULL CREATE TABLE logs.Logs(Id INT IDENTITY PRIMARY KEY,Message NVARCHAR(MAX),Level NVARCHAR(128),TimeStamp DATETIME,Exception NVARCHAR(MAX));
GO
