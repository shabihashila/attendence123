// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using FegCommunication.WebApi.Common;
using FegCommunication.WebApi.Sms;

namespace FegCommunication.WebApi.Endpoints;

public static class SmsEndpoints
{
    public static IEndpointRouteBuilder MapSmsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/sms")
            .RequireAuthorization()
            .WithTags("Sms");

        group.MapPost("/send", async (
                SendSmsRequest req,
                ISmsService sms,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await sms.SendAsync(req, ct);
                    var msg = $"Sent {result.Sent}/{result.Total}" + (result.Failed > 0 ? $" ({result.Failed} failed)" : "");
                    return Results.Ok(ApiResponse<SendSmsResult>.Ok(result, msg));
                }
                catch (UnknownSmsProviderException ex)
                {
                    return Results.BadRequest(ApiResponse.Fail(ex.Message,
                        new Dictionary<string, string[]>
                        {
                            ["provider"] = new[] { $"Unknown SMS provider. Known: {string.Join(", ", ex.Known)}" }
                        }));
                }
            })
            .ValidateBody<SendSmsRequest>()
            .Produces<ApiResponse<SendSmsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        // Convenience for callers: list provider names that this deployment
        // supports. Useful for HRAccounts's UI to populate a "Send via..."
        // dropdown without hardcoding strings.
        group.MapGet("/providers", (ISmsService sms) =>
            Results.Ok(ApiResponse<IReadOnlyCollection<string>>.Ok(sms.KnownProviders)))
            .Produces<ApiResponse<IReadOnlyCollection<string>>>(StatusCodes.Status200OK);

        return routes;
    }
}
