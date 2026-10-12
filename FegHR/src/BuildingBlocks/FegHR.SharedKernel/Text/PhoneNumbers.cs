using System.Text;

namespace FegHR.SharedKernel.Text;

/// <summary>
/// The one phone normalization rule (staff logins, customers, SMS): digits only,
/// with a Bangladeshi local mobile (<c>01XXXXXXXXX</c>) given the 880 country code.
/// </summary>
public static class PhoneNumbers
{
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;
        var digits = new StringBuilder(phone.Length);
        foreach (var c in phone)
            if (char.IsAsciiDigit(c))
                digits.Append(c);
        var value = digits.ToString();
        if (value.StartsWith("01", StringComparison.Ordinal) && value.Length == 11)
            value = "88" + value;
        return value.Length is >= 10 and <= 15 ? value : null;
    }

    /// <summary>A Bangladeshi mobile number after normalization: 8801[3-9]XXXXXXXX.</summary>
    public static bool IsBangladeshiMobile(string normalized)
        => normalized.Length == 13 && normalized.StartsWith("8801", StringComparison.Ordinal) && normalized[4] is >= '3' and <= '9';
}
