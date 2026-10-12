namespace FegHR.Modules.Iam.Features;

/// <summary>Permission codes seeded by <c>SeedData/01_PermissionsMenus.sql</c>.</summary>
internal static class IamPermissions
{
    public const string UsersRead = "iam.users.read";
    public const string UsersWrite = "iam.users.write";
    public const string RolesRead = "iam.roles.read";
    public const string RolesWrite = "iam.roles.write";
    public const string PermissionsRead = "iam.permissions.read";
}
