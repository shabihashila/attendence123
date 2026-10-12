-- Tenant isolation is enforced in SQL Server (same model as DailyCommerce and DailyRestaurant).
-- This predicate deliberately has no platform-admin bypass: the product owner reaches a company
-- through a membership and a tenant switch, never around row-level security.
-- Once policies reference it, predicate changes require a new versioned migration.
IF OBJECT_ID(N'security.fn_TenantAccess', N'IF') IS NULL
    EXEC(N'CREATE FUNCTION security.fn_TenantAccess(@TenantId int)
    RETURNS TABLE WITH SCHEMABINDING AS
    RETURN SELECT 1 AS Allowed
    WHERE @TenantId = CONVERT(int, SESSION_CONTEXT(N''FegHR.TenantId''))
      AND @TenantId > 0;');
GO
-- Every module script that adds a tenant table (a table with a TenantId column) re-runs this for
-- its schema. Global control-plane tables name the column TenantRefId and are not covered.
CREATE OR ALTER PROCEDURE security.usp_ApplyTenantPolicy
    @Schema sysname
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Schema NOT IN (N'platform',N'iam',N'org',N'attendance',N'leave')
        THROW 51000, 'Schema is not a tenant module.', 1;
    DECLARE @Table sysname, @ObjectId int, @Policy sysname, @PolicyId int, @Sql nvarchar(max);
    DECLARE tables_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT t.name, t.object_id FROM sys.tables t
        JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'TenantId'
        WHERE t.schema_id = SCHEMA_ID(@Schema) AND t.is_ms_shipped = 0 AND t.name NOT IN(N'Outbox',N'Inbox')
        ORDER BY t.name;
    OPEN tables_cursor;
    FETCH NEXT FROM tables_cursor INTO @Table, @ObjectId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF LEN(@Table) > 115 THROW 51001, 'Tenant policy name is too long.', 1;
        SET @Policy = N'TenantAccess_' + @Table;
        SET @PolicyId = OBJECT_ID(QUOTENAME(@Schema) + N'.' + QUOTENAME(@Policy));
        IF @PolicyId IS NULL
        BEGIN
            SET @Sql = N'CREATE SECURITY POLICY ' + QUOTENAME(@Schema) + N'.' + QUOTENAME(@Policy)
                + N' ADD FILTER PREDICATE security.fn_TenantAccess(TenantId) ON ' + QUOTENAME(@Schema) + N'.' + QUOTENAME(@Table)
                + N', ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON ' + QUOTENAME(@Schema) + N'.' + QUOTENAME(@Table) + N' AFTER INSERT'
                + N', ADD BLOCK PREDICATE security.fn_TenantAccess(TenantId) ON ' + QUOTENAME(@Schema) + N'.' + QUOTENAME(@Table) + N' AFTER UPDATE'
                + N' WITH (STATE = ON, SCHEMABINDING = ON);';
            EXEC sys.sp_executesql @Sql;
        END
        ELSE
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM sys.security_policies WHERE object_id=@PolicyId AND is_enabled=1 AND is_schema_bound=1)
                THROW 51002, 'Existing tenant policy is disabled or not schema-bound.', 1;
            IF (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id=@PolicyId AND target_object_id=@ObjectId
                AND predicate_definition=N'([security].[fn_TenantAccess]([TenantId]))'
                AND ((predicate_type=0 AND operation IS NULL) OR (predicate_type=1 AND operation IN (1,2)))) <> 3
                THROW 51003, 'Existing tenant policy does not match the required predicates.', 1;
            IF (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id=@PolicyId)<>3
                THROW 51004, 'Existing tenant policy has unexpected predicates.', 1;
        END;
        FETCH NEXT FROM tables_cursor INTO @Table, @ObjectId;
    END;
    CLOSE tables_cursor;
    DEALLOCATE tables_cursor;
END;
GO
-- Serilog sink target. Created here because the application login has no DDL rights.
IF OBJECT_ID(N'logs.Logs',N'U') IS NULL
CREATE TABLE logs.Logs(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Logs PRIMARY KEY,
    Message NVARCHAR(MAX) NULL,Level NVARCHAR(128) NULL,TimeStamp DATETIME NULL,Exception NVARCHAR(MAX) NULL);
GO
