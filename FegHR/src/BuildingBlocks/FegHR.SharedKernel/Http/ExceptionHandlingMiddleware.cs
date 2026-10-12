using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FegHR.SharedKernel.Http;

/// <summary>
/// Maps exceptions to the response envelope:
/// <list type="bullet">
/// <item><see cref="BusinessRuleException"/> and procedure errors raised with
/// <c>THROW 50000-59999</c> → 422 with the procedure's message.</item>
/// <item><see cref="ForbiddenException"/> → 403 (a right checked inside a service).</item>
/// <item><see cref="NotFoundException"/> → 404.</item>
/// <item><see cref="BadHttpRequestException"/> (e.g. a query value that does not bind) → its status, normally 400.</item>
/// <item><see cref="DbUpdateConcurrencyException"/> → 409.</item>
/// <item>Anything else → 500 with a generic message; the detail goes to the log only.</item>
/// </list>
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to answer.
        }
        catch (Exception ex) when (!http.Response.HasStarted)
        {
            var (status, message) = Map(ex);
            if (status >= StatusCodes.Status500InternalServerError)
                Log.ForContext("SourceContext", typeof(ExceptionHandlingMiddleware).FullName)
                    .Error(ex, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path.Value);

            http.Response.Clear();
            http.Response.StatusCode = status;
            await http.Response.WriteAsJsonAsync(ApiResponse.Fail(message), ApiJson.Options);
        }
    }

    private static (int Status, string Message) Map(Exception ex) => ex switch
    {
        BusinessRuleException br => (StatusCodes.Status422UnprocessableEntity, br.Message),
        ForbiddenException fe => (StatusCodes.Status403Forbidden, fe.Message),
        NotFoundException nf => (StatusCodes.Status404NotFound, nf.Message),
        BadHttpRequestException bad => (bad.StatusCode, "The request is not valid. Check the values sent."),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
            "Someone else changed this record. Reload and try again."),
        _ when FindProcedureError(ex) is { } sql => (StatusCodes.Status422UnprocessableEntity, sql.Message),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong. Please try again.")
    };

    // User-defined errors from THROW are numbered 50000 and above. The
    // versioned scripts keep business errors in 50000-59999.
    private static SqlException? FindProcedureError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number is >= 50000 and <= 59999)
                return sql;
        return null;
    }
}
