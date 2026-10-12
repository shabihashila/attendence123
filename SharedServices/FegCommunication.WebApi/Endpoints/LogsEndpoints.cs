// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using FegCommunication.WebApi.Common;
using FegCommunication.WebApi.Logs;
using Microsoft.AspNetCore.Http;

namespace FegCommunication.WebApi.Endpoints;

// Read-only audit endpoint. HRAccounts's UI proxies this so HR can see the
// "what got sent" log without giving the comm service direct exposure
// to end-users.
public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/logs")
            .RequireAuthorization()
            .WithTags("Logs");

        group.MapGet("/", async (
                [AsParameters] LogFilterQuery q,
                ICommunicationLogStore store,
                CancellationToken ct) =>
            {
                var result = await store.ListAsync(q, ct);
                return Results.Ok(ApiResponse<PagedLogResult>.Ok(result));
            })
            .Produces<ApiResponse<PagedLogResult>>(StatusCodes.Status200OK);

        return routes;
    }
}
