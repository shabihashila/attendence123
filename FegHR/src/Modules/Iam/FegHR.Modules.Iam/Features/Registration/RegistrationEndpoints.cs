using System.ComponentModel.DataAnnotations;
using System.Text;
using FegHR.Modules.Iam.Features.Auth;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Iam.Security;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Serilog;
using Shared.Time;

namespace FegHR.Modules.Iam.Features.Registration;

/// <summary>
/// Public sign-up, as in DailyCommerce: the person registering becomes the administrator of a
/// new company, which starts on a free trial licence. Nothing in the request can name an
/// existing company, a role or the product-owner flag.
/// </summary>
internal sealed record RegisterCompanyRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string CompanyName,
    [property: StringLength(500)] string? CompanyAddress,
    [property: StringLength(30)] string? CompanyPhone,
    [property: Required, StringLength(150, MinimumLength = 2)] string FullName,
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: StringLength(30)] string? Phone,
    [property: Required, StringLength(128)] string Password);

/// <summary>The new administrator is signed in straight away.</summary>
internal sealed record RegisterCompanyResponse(AuthSession Session, DateTimeOffset TrialEndsAt);

internal static class RegistrationEndpoints
{
    private const int EmailOrPhoneTaken = 51030;
    private const int RegistrationClosed = 51050;
    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(RegistrationEndpoints).FullName);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/auth/register", async (RegisterCompanyRequest request, IamProcedures procedures, AuthService auth,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (PasswordHasher.Validate(request.Password) is { } passwordProblem)
                    return ApiResults.Fail(passwordProblem, StatusCodes.Status400BadRequest,
                        new Dictionary<string, string[]> { ["password"] = [passwordProblem] });
                string? phone = null;
                if (!string.IsNullOrWhiteSpace(request.Phone) && (phone = LoginNormalizer.NormalizePhone(request.Phone)) is null)
                    return ApiResults.Fail("Enter a valid mobile number, like 01712345678.", StatusCodes.Status400BadRequest,
                        new Dictionary<string, string[]> { ["phone"] = ["Enter a valid mobile number, like 01712345678."] });
                if (request.CompanyName.Any(char.IsControl) || request.FullName.Any(char.IsControl))
                    return ApiResults.Fail("Names cannot contain control characters.");

                var now = clock.UtcNowOffset();
                RegisteredCompany created;
                try
                {
                    created = await procedures.RegisterCompanyAsync(new
                    {
                        CompanyName = request.CompanyName.Trim(),
                        BaseSlug = SlugOf(request.CompanyName),
                        CompanyAddress = request.CompanyAddress,
                        CompanyPhone = request.CompanyPhone,
                        CompanyEmail = request.Email.Trim(),
                        AdminName = request.FullName.Trim(),
                        AdminEmail = request.Email.Trim(),
                        AdminPhone = phone,
                        PasswordHash = PasswordHasher.Hash(request.Password),
                        // The company starts in the default time zone; its date decides the joining date.
                        Today = new TenantClock("Asia/Dhaka").DateOf(now).ToDateTime(TimeOnly.MinValue),
                        Now = now
                    }, ct);
                }
                catch (SqlException ex) when (ex.Number == EmailOrPhoneTaken)
                {
                    return ApiResults.Fail("An account with this email or phone already exists. Please sign in.",
                        StatusCodes.Status409Conflict);
                }
                catch (SqlException ex) when (ex.Number == RegistrationClosed)
                {
                    return ApiResults.Fail(ex.Message, StatusCodes.Status403Forbidden);
                }

                Logger.Information("Company {TenantId} ({Slug}) registered by user {UserId}", created.TenantId, created.Slug, created.UserId);
                var session = await auth.IssueSessionAsync(created.UserId, request.FullName.Trim(), false,
                    new MembershipRecord(created.TenantId, created.Slug, request.CompanyName.Trim(), created.TenantUserId, null, null, false), ct);
                return ApiResults.Ok(new RegisterCompanyResponse(session, created.TrialEndsAt),
                    "Your company is ready. Welcome to your free trial.");
            })
            .AllowAnonymous()
            .SkipLicenseCheck()
            .Validate<RegisterCompanyRequest>()
            .RequireRateLimiting(RateLimitPolicies.Registration)
            .WithName("RegisterCompany");
    }

    /// <summary>
    /// A URL-safe company address from its name: ASCII letters and digits, dashes between words,
    /// at most 60 characters. The database appends a number when the address is taken.
    /// </summary>
    internal static string SlugOf(string name)
    {
        var slug = new StringBuilder(60);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (slug.Length >= 60)
                break;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? "company" : result;
    }
}
