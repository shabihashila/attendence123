using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FegHR.Modules.Platform.Features.Tenant;

internal sealed record GroupContext(int GroupId, string Name, bool CanManage, bool CanBill);
internal sealed record GroupMember(int UserId, string DisplayName, string Email, string RoleCode, bool IsActive);
internal sealed record GroupCompany(int Id, string Name, int? Employees, int? Users, int? AttendanceDays, int? PendingLeave, string? LicenseKind, DateTimeOffset? LicenseEndsAt,byte[] RowVersion);
internal sealed record RenameGroupCompany([property:Required,StringLength(200,MinimumLength=2)] string Name,[property:Required] string RowVersion);
internal sealed record NewGroupConcern([property:Required,StringLength(200,MinimumLength=2)] string Name);
internal sealed record ConcernResult(int Id,string Slug,DateTimeOffset TrialEndsAt);
internal sealed record GroupAudit(long Id, string Actor, string Action, string? Detail, DateTimeOffset OccurredAt);
internal sealed record SettingLayers(string GlobalDefaults, string GroupDefaults, string CompanyOverrides, byte[] GlobalRowVersion, byte[]? GroupRowVersion, byte[]? CompanyRowVersion);
internal sealed record BillingProfile(string BillingName, string Email, string? Address, byte[] RowVersion);
internal sealed record SaveBilling([property:Required,StringLength(200)] string BillingName,[property:Required,EmailAddress,StringLength(254)] string Email,[property:StringLength(500)] string? Address,string? RowVersion);
internal sealed record SaveGroupMember([property: Required, EmailAddress] string Email,
    [property: Required, RegularExpression("^(group-admin|group-viewer|group-billing)$")] string RoleCode, bool IsActive);
internal sealed record SaveSettings([property: Required, StringLength(10000)] string SettingsJson, bool CompanyOverride, bool GlobalOverride, string? RowVersion);

internal static class GroupEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/groups").WithTags("Groups").RequireStaff().SkipLicenseCheck();
        group.MapPost("/concerns", async(NewGroupConcern request,ITenantDbConnectionFactory factory,CancellationToken ct)=>
        {
            if(request.Name.Trim().Length<2 || request.Name.Any(char.IsControl)) return ApiResults.Fail("Enter a valid company name.");
            var slug=Regex.Replace(request.Name.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
            if(slug.Length>60) slug=slug[..60].TrimEnd('-');
            await using var c=await factory.OpenAsync(ct);
            return ApiResults.Ok(await c.QuerySingleAsync<ConcernResult>(Command("platform.usp_Group_Concern_Create",new {request.Name,BaseSlug=slug.Length==0?"company":slug,Now=DateTimeOffset.UtcNow},ct)),"Child concern created.");
        }).Validate<NewGroupConcern>();
        group.MapPut("/companies/{id:int}/name",async(int id,RenameGroupCompany request,ITenantDbConnectionFactory factory,CancellationToken ct)=>
        {
            if(request.Name.Trim().Length<2 || request.Name.Any(char.IsControl)) return ApiResults.Fail("Enter a valid company name.");
            byte[] version;
            try{version=Convert.FromBase64String(request.RowVersion);}catch(FormatException){return ApiResults.Fail("Reload the company.");}
            if(version.Length!=8) return ApiResults.Fail("Reload the company.");
            await using var c=await factory.OpenAsync(ct);
            var p=new DynamicParameters(new {TenantId=id,request.Name});p.Add("RowVersion",version,DbType.Binary,size:8);
            await c.ExecuteAsync(Command("platform.usp_Group_Company_Rename",p,ct));
            return ApiResults.Ok("Company name saved.");
        }).Validate<RenameGroupCompany>();
        group.MapGet("/current", async (ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok(await c.QuerySingleAsync<GroupContext>(Command("platform.usp_Group_Context", null, ct)));
        });
        group.MapGet("/companies", async (ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok((await c.QueryAsync<GroupCompany>(Command("platform.usp_Group_Report", null, ct))).ToList());
        });
        group.MapGet("/members", async (ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok((await c.QueryAsync<GroupMember>(Command("iam.usp_Group_Members", null, ct))).ToList());
        });
        group.MapPut("/members", async (SaveGroupMember request, ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok((await c.QueryAsync<GroupMember>(Command("iam.usp_Group_Members", new { request.Email, request.RoleCode, request.IsActive }, ct))).ToList(), "Group access saved.");
        }).Validate<SaveGroupMember>();
        group.MapGet("/audit", async (ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok((await c.QueryAsync<GroupAudit>(Command("platform.usp_Group_Audit", null, ct))).ToList());
        });
        group.MapGet("/settings", async (ITenantDbConnectionFactory factory, CancellationToken ct) => await SettingsAsync(factory, null, ct));
        group.MapGet("/billing", async (ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            await using var c = await factory.OpenAsync(ct);
            return ApiResults.Ok(await c.QuerySingleOrDefaultAsync<BillingProfile>(Command("platform.usp_Group_Billing",null,ct)));
        });
        group.MapPut("/billing", async (SaveBilling request, ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            byte[]? version;
            try { version=request.RowVersion is null ? null : Convert.FromBase64String(request.RowVersion); }
            catch(FormatException) {return ApiResults.Fail("Reload the billing profile.");}
            await using var c=await factory.OpenAsync(ct);
            var p=new DynamicParameters(new {request.BillingName,request.Email,request.Address}); p.Add("RowVersion",version,DbType.Binary,size:8);
            return ApiResults.Ok(await c.QuerySingleAsync<BillingProfile>(Command("platform.usp_Group_Billing",p,ct)),"Billing profile saved.");
        }).Validate<SaveBilling>();
        group.MapPut("/settings", async (SaveSettings request, ITenantDbConnectionFactory factory, CancellationToken ct) =>
        {
            try
            {
                using var json = JsonDocument.Parse(request.SettingsJson);
                if (json.RootElement.ValueKind != JsonValueKind.Object) return ApiResults.Fail("Settings must be a JSON object.");
            }
            catch (JsonException) { return ApiResults.Fail("Enter valid JSON settings."); }
            return await SettingsAsync(factory, request, ct);
        }).Validate<SaveSettings>();
    }

    private static CommandDefinition Command(string proc, object? parameters, CancellationToken ct)
        => new(proc, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static async Task<IResult> SettingsAsync(ITenantDbConnectionFactory factory, SaveSettings? request, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        byte[]? version;
        try { version = request?.RowVersion is null ? null : Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return ApiResults.Fail("Reload the settings."); }
        var p = new DynamicParameters(request is null ? null : new { request.SettingsJson, request.CompanyOverride, request.GlobalOverride });
        p.Add("RowVersion",version,DbType.Binary,size:8);
        var layers = await c.QuerySingleAsync<SettingLayers>(Command("platform.usp_Group_Settings", p, ct));
        var effective = new Dictionary<string, JsonElement>();
        foreach (var json in new[] { layers.GlobalDefaults, layers.GroupDefaults, layers.CompanyOverrides })
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject()) effective[property.Name] = property.Value.Clone();
        }
        return ApiResults.Ok(new { layers.GlobalDefaults,layers.GroupDefaults, layers.CompanyOverrides, layers.GlobalRowVersion,layers.GroupRowVersion,layers.CompanyRowVersion,effective }, request is null ? "Settings loaded." : "Settings saved.");
    }
}
