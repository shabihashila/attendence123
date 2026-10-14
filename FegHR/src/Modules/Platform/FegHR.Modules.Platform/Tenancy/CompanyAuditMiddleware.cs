using System.Data;
using Dapper;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace FegHR.Modules.Platform.Tenancy;

public sealed class CompanyAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http,ITenantContext tenant,ITenantDbConnectionFactory connections)
    {
        var record=tenant.UserId is > 0 && tenant.TenantId is > 0 && http.Request.Path.StartsWithSegments("/api")
            && !http.Request.Path.StartsWithSegments("/api/iam/auth") && !http.Request.Path.StartsWithSegments("/api/iam/session")
            && http.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
        var completed=false;
        try {await next(http);completed=true;}
        finally
        {
            if(record)
            {
                try
                {
                    await using var c=await connections.OpenAsync(CancellationToken.None);
                    var path=http.Request.Path.Value ?? string.Empty;
                    await c.ExecuteAsync(new CommandDefinition("platform.usp_RequestAudit_Add",new {Method=http.Request.Method,Path=path.Length>500?path[..500]:path,
                        ResponseStatus=completed?http.Response.StatusCode:500,TraceId=http.TraceIdentifier},commandType:CommandType.StoredProcedure,commandTimeout:5));
                }
                catch(Exception ex){Log.Warning(ex,"Company audit write failed for tenant {TenantId} trace {TraceId}",tenant.TenantId,http.TraceIdentifier);}
            }
        }
    }
}
