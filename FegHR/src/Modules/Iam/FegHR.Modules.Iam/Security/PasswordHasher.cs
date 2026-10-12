namespace FegHR.Modules.Iam.Security;

/// <summary>BCrypt (cost 12), the same scheme CompanyHub used.</summary>
internal static class PasswordHasher
{
    private const int WorkFactor = 12;

    // A fixed hash to verify against when the login does not exist, so a
    // wrong login and a wrong password take the same time.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing", WorkFactor);

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            return false;
        }
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>Minimum policy: 8+ characters with a letter and a digit.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return "Use at least 8 characters.";
        if (password.Length > 128)
            return "Use at most 128 characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Use at least one letter and one digit.";
        return null;
    }
}
