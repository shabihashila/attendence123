namespace FegHR.Modules.Iam.Security;

/// <summary>
/// The one normalization rule for sign-in identifiers, mirrored by the
/// iam procedures: email → trimmed upper case; Bangladeshi mobile →
/// digits only with the 880 country code (<c>01712345678</c> → <c>8801712345678</c>).
/// </summary>
internal static class LoginNormalizer
{
    public static string? Normalize(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return null;
        var trimmed = login.Trim();
        return trimmed.Contains('@') ? NormalizeEmail(trimmed) : NormalizePhone(trimmed);
    }

    public static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    public static string? NormalizePhone(string? phone) => SharedKernel.Text.PhoneNumbers.Normalize(phone);
}
