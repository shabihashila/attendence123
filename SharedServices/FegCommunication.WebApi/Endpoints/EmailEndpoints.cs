// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using FegCommunication.WebApi.Common;
using FegCommunication.WebApi.Email;

namespace FegCommunication.WebApi.Endpoints;

public static class EmailEndpoints
{
    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/email")
            .RequireAuthorization()
            .WithTags("Email");

        group.MapPost("/send", async (
                SendEmailRequest req,
                IEmailService email,
                CancellationToken ct) =>
            {
                if (req.To.Count == 0 && req.Cc.Count == 0 && req.Bcc.Count == 0)
                    return Results.BadRequest(ApiResponse.Fail("At least one recipient is required (To / Cc / Bcc)"));

                var result = await email.SendAsync(req, ct);
                var msg = $"Sent {result.Sent}/{result.Total}" + (result.Failed > 0 ? $" ({result.Failed} failed)" : "");
                return Results.Ok(ApiResponse<SendEmailResult>.Ok(result, msg));
            })
            .ValidateBody<SendEmailRequest>()
            .Produces<ApiResponse<SendEmailResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        return routes;
    }
}
