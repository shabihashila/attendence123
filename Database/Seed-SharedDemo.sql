:on error exit
SET NOCOUNT ON;
IF DB_NAME()=N'db_feg_communication'
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dbo.CommunicationLog WHERE Subject=N'FegHR demo welcome')
        INSERT dbo.CommunicationLog(Kind,Recipient,Subject,Sender,Status,Provider,CallerKey)
        VALUES(N'Email',N'Administrator@feghr.com',N'FegHR demo welcome',N'no-reply@feghr.com',N'Sent',N'Demo log only',N'FegHR');
END;
GO
IF DB_NAME()=N'db_feg_storage'
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dbo.FileOperationLog WHERE Container=N'feghr' AND [Key]=N'demo/readme.txt')
        INSERT dbo.FileOperationLog(Kind,Provider,Container,[Key],SizeBytes,Status,CallerKey)
        VALUES(N'Upload',N'Local',N'feghr',N'demo/readme.txt',24,N'Ok',N'FegHR');
END;
GO
IF NOT EXISTS(SELECT 1 FROM logs.Logs WHERE Message=N'FegHR shared service demo ready')
    INSERT logs.Logs(Message,Level,TimeStamp) VALUES(N'FegHR shared service demo ready',N'Information',GETDATE());
GO
