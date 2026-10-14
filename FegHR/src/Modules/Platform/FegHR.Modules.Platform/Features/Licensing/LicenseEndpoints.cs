using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Platform.Contracts;
using FegHR.Modules.Platform.Persistence;
using FegHR.Modules.Platform.Tenancy;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Shared.Time;

namespace FegHR.Modules.Platform.Features.Licensing;

internal sealed record LicenseView(bool IsLicensed, string? Kind, string? PlanName, DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt, int? DaysLeft, string TenantStatus);

internal sealed record OwnerCompanyDto(int Id, string Slug, string Name, string? Phone, string? Email, string Status,
    string Origin, DateTimeOffset CreatedAt, bool IsLicensed, string? LicenseKind, string? PlanName, DateTimeOffset? LicenseEndsAt,
    bool IsOwnerCompany, int? ParentTenantRefId, string? ParentName, int? MaxChildConcerns, int ChildCount, string RowVersion);

internal sealed record SaveCompanyRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: Range(1, int.MaxValue)] int? ParentTenantRefId,
    [property: Range(0, int.MaxValue)] int? MaxChildConcerns,
    [property: Required] string RowVersion);

internal sealed record CreateConcernRequest([property: Required, StringLength(200, MinimumLength = 2)] string Name);

internal sealed record SetCompanyStatusRequest(
    [property: Required, RegularExpression("^(Active|Suspended|Closed)$", ErrorMessage = "Choose Active, Suspended or Closed.")] string Status);

internal sealed record IssueLicenseRequest(
    [property: Required, StringLength(60)] string PlanCode,
    [property: Required, RegularExpression("^(Trial|Paid|Complimentary)$", ErrorMessage = "Choose Trial, Paid or Complimentary.")] string Kind,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    [property: StringLength(100)] string? LicenseKey,
    [property: StringLength(500)] string? Note);

internal sealed record LicensePolicyDto(int TrialDays, string TrialPlanCode, bool RegistrationOpen, string RowVersion);

internal sealed record SaveLicensePolicyRequest(
    [property: Range(1, 3650)] int TrialDays,
    [property: Required, StringLength(60)] string TrialPlanCode,
    bool RegistrationOpen,
    [property: Required] string RowVersion);

/// <summary>
/// Licensing in the DailyCommerce manner: a public registration receives a trial licence whose
/// length the product owner sets; the product owner then issues paid or complimentary licences,
/// revokes them and suspends companies. A company without a licence in force is refused by the
/// licence guard and can still open this page to see why.
/// </summary>
internal static class LicenseEndpoints
{
    public static void Map(RouteGroupBuilder staff, IEndpointRouteBuilder routes)
    {
        // The signed-in company's own licence: shown in the shell and on the licence page.
        staff.MapGet("/licence", async (ITenantDirectory tenants, IAppTimeProvider clock, CancellationToken ct) =>
                await tenants.GetLicenseStatusAsync(ct) is { } s
                    ? ApiResults.Ok(new LicenseView(s.IsLicensed, s.Kind, s.PlanName, s.StartsAt, s.EndsAt,
                        s.DaysLeft(clock.UtcNowOffset()), s.TenantStatus))
                    : ApiResults.NotFound("The company"))
            .RequireStaff()
            .SkipLicenseCheck()
            .WithName("GetLicence");

        // What the registration page promises before a company exists.
        routes.MapGet("/api/public/registration-offer", async (PlatformProcedures procedures, CancellationToken ct) =>
                ApiResults.Ok(await procedures.GetRegistrationOfferAsync(ct) ?? new RegistrationOffer(false, 0, string.Empty)))
            .AllowAnonymous()
            .WithTags("Platform")
            .WithName("GetRegistrationOffer");

        var owner = routes.MapGroup("/api/owner").WithTags("Owner").RequirePlatformAdmin().SkipLicenseCheck();

        owner.MapGet("/companies", async ([AsParameters] PageRequest page, PlatformProcedures procedures, IAppTimeProvider clock,
                CancellationToken ct) =>
            {
                var rows = await procedures.OwnerListTenantsAsync(clock.UtcNowOffset(), page.Search, page.Skip, page.SafePageSize, ct);
                var items = rows.Select(r => new OwnerCompanyDto(r.Id, r.Slug, r.Name, r.Phone, r.Email, r.Status, r.Origin,
                    r.CreatedAt, r.IsLicensed, r.LicenseKind, r.PlanName, r.LicenseEndsAt, r.IsOwnerCompany,
                    r.ParentTenantRefId, r.ParentName, r.MaxChildConcerns, r.ChildCount, Convert.ToBase64String(r.RowVersion))).ToList();
                return ApiResults.Ok(new PagedResult<OwnerCompanyDto>(items, rows.Count > 0 ? rows[0].TotalCount : 0,
                    page.SafePage, page.SafePageSize));
            })
            .WithName("OwnerListCompanies");

        owner.MapGet("/companies/main", async (PlatformProcedures procedures, CancellationToken ct) =>
                ApiResults.Ok(await procedures.OwnerListMainCompaniesAsync(ct)))
            .WithName("OwnerListMainCompanies");

        owner.MapPut("/companies/{id:int}", async (int id, SaveCompanyRequest request, PlatformProcedures procedures,
                ITenantContext tenant, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (request.Name.Trim().Length < 2 || request.Name.Any(char.IsControl))
                    return ApiResults.Fail("Enter a company name of at least two characters without control characters.");
                byte[] version;
                try { version = Convert.FromBase64String(request.RowVersion); }
                catch (FormatException) { return ApiResults.Fail("Reload the company before saving."); }
                if (version.Length != 8) return ApiResults.Fail("Reload the company before saving.");
                try
                {
                    await procedures.OwnerSaveTenantAsync(id, request.Name.Trim(), request.ParentTenantRefId,
                        request.MaxChildConcerns, version, clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                }
                catch (SqlException ex) when (ex.Number == 51062) { return ApiResults.Conflict(ex.Message); }
                return ApiResults.Ok("Company saved.");
            })
            .Validate<SaveCompanyRequest>()
            .WithName("OwnerSaveCompany");

        owner.MapPost("/companies/{id:int}/concerns", async (int id, CreateConcernRequest request, PlatformProcedures procedures,
                ITenantContext tenant, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var name = request.Name.Trim();
                if (name.Length < 2 || name.Any(char.IsControl))
                    return ApiResults.Fail("Enter a company name of at least two characters without control characters.");
                var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
                var created = await procedures.OwnerCreateConcernAsync(id, name, slug.Length == 0 ? "company" : slug,
                    clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                return ApiResults.Ok(created!, "Child concern created. Open it to add users and employees.");
            })
            .Validate<CreateConcernRequest>()
            .WithName("OwnerCreateConcern");

        owner.MapPut("/companies/{id:int}/status", async (int id, SetCompanyStatusRequest request, PlatformProcedures procedures,
                ITenantContext tenant, IMemoryCache cache, IAppTimeProvider clock, CancellationToken ct) =>
            {
                await procedures.OwnerSetTenantStatusAsync(id, request.Status, clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                cache.Remove(LicenseGate.CacheKey(id));
                return ApiResults.Ok("Company status saved.");
            })
            .Validate<SetCompanyStatusRequest>()
            .WithName("OwnerSetCompanyStatus");

        owner.MapGet("/companies/{id:int}/licences", async (int id, PlatformProcedures procedures, CancellationToken ct) =>
                ApiResults.Ok(await procedures.OwnerListLicensesAsync(id, ct)))
            .WithName("OwnerListLicences");

        owner.MapPost("/companies/{id:int}/licences", async (int id, IssueLicenseRequest request, PlatformProcedures procedures,
                ITenantContext tenant, IMemoryCache cache, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var now = clock.UtcNowOffset();
                var starts = request.StartsAt ?? now;
                if (request.EndsAt is { } ends && ends <= starts)
                    return ApiResults.Fail("The licence must end after it starts.");
                await procedures.OwnerIssueLicenseAsync(id, request.PlanCode, request.Kind, starts, request.EndsAt,
                    request.LicenseKey, request.Note, now, tenant.UserId!.Value, ct);
                cache.Remove(LicenseGate.CacheKey(id));
                return ApiResults.Ok("Licence issued.");
            })
            .Validate<IssueLicenseRequest>()
            .WithName("OwnerIssueLicence");

        owner.MapDelete("/companies/{id:int}/licences/{licenceId:int}", async (int id, int licenceId, PlatformProcedures procedures,
                ITenantContext tenant, IMemoryCache cache, IAppTimeProvider clock, CancellationToken ct) =>
            {
                await procedures.OwnerRevokeLicenseAsync(licenceId, clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                cache.Remove(LicenseGate.CacheKey(id));
                return ApiResults.Ok("Licence revoked.");
            })
            .WithName("OwnerRevokeLicence");

        owner.MapGet("/plans", async (PlatformDbContext db, CancellationToken ct) =>
                ApiResults.Ok(await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id)
                    .Select(p => new { p.Code, p.Name, p.MonthlyPrice, p.MaxEmployees }).ToListAsync(ct)))
            .WithName("OwnerListPlans");

        owner.MapGet("/licence-policy", async (PlatformProcedures procedures, CancellationToken ct) =>
                await procedures.OwnerGetPolicyAsync(ct) is { } p
                    ? ApiResults.Ok(new LicensePolicyDto(p.TrialDays, p.TrialPlanCode, p.RegistrationOpen, Convert.ToBase64String(p.RowVersion)))
                    : ApiResults.NotFound("The licence policy"))
            .WithName("OwnerGetLicencePolicy");

        owner.MapPut("/licence-policy", async (SaveLicensePolicyRequest request, PlatformProcedures procedures, ITenantContext tenant,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                byte[] version;
                try { version = Convert.FromBase64String(request.RowVersion); }
                catch (FormatException) { return ApiResults.Fail("Reload the policy before saving."); }
                if (version.Length != 8)
                    return ApiResults.Fail("Reload the policy before saving.");
                await procedures.OwnerSetPolicyAsync(request.TrialDays, request.TrialPlanCode, request.RegistrationOpen, version,
                    clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                var saved = await procedures.OwnerGetPolicyAsync(ct);
                return ApiResults.Ok(new LicensePolicyDto(saved!.TrialDays, saved.TrialPlanCode, saved.RegistrationOpen,
                    Convert.ToBase64String(saved.RowVersion)), "Licence policy saved. It applies to new registrations.");
            })
            .Validate<SaveLicensePolicyRequest>()
            .WithName("OwnerSaveLicencePolicy");
    }
}
