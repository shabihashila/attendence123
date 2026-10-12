// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using FegStorage.WebApi.Common;
using FegStorage.WebApi.Logs;
using Microsoft.AspNetCore.Http;

namespace FegStorage.WebApi.Endpoints;

// Read-only audit endpoint. HRAccounts's /admin/logs UI can proxy this so
// HR can see "what got uploaded / downloaded / deleted" without giving
// the file service direct exposure to end-users.
public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/logs")
            .RequireAuthorization()
            .WithTags("Logs");

        group.MapGet("/", async (
                [AsParameters] LogFilterQuery q,
                IFileOperationLogStore store,
                CancellationToken ct) =>
            {
                var result = await store.ListAsync(q, ct);
                return Results.Ok(ApiResponse<PagedLogResult>.Ok(result));
            })
            .Produces<ApiResponse<PagedLogResult>>(StatusCodes.Status200OK);

        return routes;
    }
}
