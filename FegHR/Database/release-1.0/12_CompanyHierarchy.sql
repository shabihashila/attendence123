-- Company hierarchy is metadata; tenant permissions and RLS remain independent.
IF COL_LENGTH(N'platform.Tenants',N'ParentTenantRefId') IS NULL
    ALTER TABLE platform.Tenants ADD ParentTenantRefId INT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Tenants_Parent')
    ALTER TABLE platform.Tenants ADD CONSTRAINT FK_Tenants_Parent FOREIGN KEY(ParentTenantRefId) REFERENCES platform.Tenants(Id);
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Tenants_NotOwnParent')
    ALTER TABLE platform.Tenants ADD CONSTRAINT CK_Tenants_NotOwnParent CHECK(ParentTenantRefId IS NULL OR ParentTenantRefId<>Id);
GO
