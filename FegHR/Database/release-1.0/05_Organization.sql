-- org: departments, designations and employees. BranchId refers to platform.Branches and UserId
-- to iam.Users by id only (no cross-schema foreign keys, ARCHITECTURE.md section 3 rule 4).
IF OBJECT_ID(N'org.Departments', N'U') IS NULL
BEGIN
    CREATE TABLE [org].[Departments](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Departments_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Name NVARCHAR(150) NOT NULL,
        CONSTRAINT UQ_Departments_TenantId_Name UNIQUE(TenantId,Name),
        IsActive BIT NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Departments_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Departments_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'org.Designations', N'U') IS NULL
BEGIN
    CREATE TABLE [org].[Designations](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Designations PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Designations_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Name NVARCHAR(150) NOT NULL,
        CONSTRAINT UQ_Designations_TenantId_Name UNIQUE(TenantId,Name),
        IsActive BIT NOT NULL CONSTRAINT DF_Designations_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Designations_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Designations_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'org.Employees', N'U') IS NULL
BEGIN
    CREATE TABLE [org].[Employees](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Employees_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Code NVARCHAR(50) NOT NULL,
        FullName NVARCHAR(200) NOT NULL,
        Gender NVARCHAR(20) NULL,
        DateOfBirth DATE NULL,
        Phone NVARCHAR(30) NULL,
        Email NVARCHAR(254) NULL,
        JoiningDate DATE NULL,
        ExitDate DATE NULL,
        EmploymentType NVARCHAR(50) NULL,
        BranchId INT NULL,
        DepartmentId INT NULL,
        DesignationId INT NULL,
        ReportsToId INT NULL,      -- line manager: first leave approval step
        UserId INT NULL,           -- iam.Users.Id of the employee's own sign-in
        PhotoKey NVARCHAR(400) NULL,
        CONSTRAINT UQ_Employees_TenantId_Code UNIQUE(TenantId,Code),
        CONSTRAINT FK_Employees_Departments FOREIGN KEY(TenantId,DepartmentId) REFERENCES org.Departments(TenantId,Id),
        CONSTRAINT FK_Employees_Designations FOREIGN KEY(TenantId,DesignationId) REFERENCES org.Designations(TenantId,Id),
        CONSTRAINT CK_Employees_Dates CHECK(ExitDate IS NULL OR JoiningDate IS NULL OR ExitDate>=JoiningDate),
        IsActive BIT NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Employees_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Employees_TenantId_Id UNIQUE(TenantId, Id)
    );
    ALTER TABLE org.Employees ADD CONSTRAINT FK_Employees_ReportsTo FOREIGN KEY(TenantId,ReportsToId) REFERENCES org.Employees(TenantId,Id);
    -- One employee record per sign-in within a company.
    CREATE UNIQUE INDEX UQ_Employees_TenantId_UserId ON org.Employees(TenantId,UserId) WHERE UserId IS NOT NULL;
    CREATE INDEX IX_Employees_TenantId_DepartmentId ON org.Employees(TenantId,DepartmentId) INCLUDE(IsActive,BranchId);
    CREATE INDEX IX_Employees_TenantId_ReportsToId ON org.Employees(TenantId,ReportsToId) WHERE ReportsToId IS NOT NULL;
END;
GO
EXEC security.usp_ApplyTenantPolicy @Schema=N'org';
GO
-- Next free employee code of the bound company: EMP0001, EMP0002, ...
CREATE OR ALTER PROCEDURE org.usp_Employee_NextCode
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Max INT=ISNULL((SELECT MAX(TRY_CONVERT(int,SUBSTRING(Code,4,20))) FROM org.Employees
                             WHERE Code LIKE N'EMP[0-9]%' AND TRY_CONVERT(int,SUBSTRING(Code,4,20)) IS NOT NULL),0);
    SELECT CONCAT(N'EMP',RIGHT(CONCAT(N'0000',@Max+1),CASE WHEN @Max+1>9999 THEN LEN(@Max+1) ELSE 4 END)) AS Code;
END;
GO
-- First employee of a new company: the registering administrator. Called by registration only.
CREATE OR ALTER PROCEDURE org.usp_Employee_CreateForUser
    @UserId INT,@FullName NVARCHAR(200),@Phone NVARCHAR(30)=NULL,@Email NVARCHAR(254)=NULL,
    @BranchId INT=NULL,@JoiningDate DATE,@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0 OR @Now IS NULL
        THROW 53001,'Tenant context and timestamp are required.',1;
    DECLARE @Dept INT,@Desig INT;
    SELECT @Dept=Id FROM org.Departments WHERE Name=N'Administration';
    IF @Dept IS NULL BEGIN INSERT org.Departments(Name,CreatedAt,CreatedBy) VALUES(N'Administration',@Now,@UserId); SET @Dept=CONVERT(int,SCOPE_IDENTITY()); END;
    SELECT @Desig=Id FROM org.Designations WHERE Name=N'Administrator';
    IF @Desig IS NULL BEGIN INSERT org.Designations(Name,CreatedAt,CreatedBy) VALUES(N'Administrator',@Now,@UserId); SET @Desig=CONVERT(int,SCOPE_IDENTITY()); END;
    IF EXISTS(SELECT 1 FROM org.Employees WHERE UserId=@UserId) RETURN;
    DECLARE @Code TABLE(Code NVARCHAR(50));
    INSERT @Code EXEC org.usp_Employee_NextCode;
    INSERT org.Employees(Code,FullName,Phone,Email,JoiningDate,EmploymentType,BranchId,DepartmentId,DesignationId,UserId,CreatedAt,CreatedBy)
    SELECT Code,LTRIM(RTRIM(@FullName)),@Phone,@Email,@JoiningDate,N'Permanent',@BranchId,@Dept,@Desig,@UserId,@Now,@UserId FROM @Code;
END;
GO
