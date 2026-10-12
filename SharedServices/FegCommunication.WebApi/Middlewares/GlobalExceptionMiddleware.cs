// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using System.Net;
using System.Text.Json;
using FegCommunication.WebApi.Common;
using Serilog;

namespace FegCommunication.WebApi.Middlewares;

// Mirrors HRAccounts.WebApi/Middlewares/GlobalExceptionMiddleware so logs +
// error response shape are identical across both services. Self-
// contained -- does not reference HRAccounts.
public class GlobalExceptionMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Unhandled exception on {Method} {Path} for caller {Caller}",
                context.Request.Method,
                context.Request.Path,
                context.User?.Identity?.Name ?? "anonymous");

            await WriteErrorResponseAsync(context, ex);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        if (context.Response.HasStarted) return;

        var (status, message) = ex switch
        {
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized"),
            KeyNotFoundException        => ((int)HttpStatusCode.NotFound,     "Resource not found"),
            ArgumentException a         => ((int)HttpStatusCode.BadRequest,   a.Message),
            InvalidOperationException io => ((int)HttpStatusCode.BadRequest,  io.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = status;

        var payload = ApiResponse.Fail(message);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, options));
    }
}
