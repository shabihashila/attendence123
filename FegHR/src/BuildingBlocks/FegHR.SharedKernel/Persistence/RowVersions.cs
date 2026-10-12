using Microsoft.EntityFrameworkCore;

namespace FegHR.SharedKernel.Persistence;

/// <summary>
/// Optimistic concurrency for updates: the client sends back the
/// <c>rowVersion</c> it read (base64), and EF compares it on save. A mismatch
/// raises <see cref="DbUpdateConcurrencyException"/>, which the exception
/// middleware maps to 409.
/// </summary>
public static class RowVersions
{
    /// <summary>Sets the original row version for the update. False when the value is missing or not base64.</summary>
    public static bool TryApply<TEntity>(DbContext db, TEntity entity, string? rowVersion)
        where TEntity : class, IVersioned
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            return false;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            return false;
        }
        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = bytes;
        return true;
    }

    public static string ToBase64(this IVersioned entity) => Convert.ToBase64String(entity.RowVersion);
}
