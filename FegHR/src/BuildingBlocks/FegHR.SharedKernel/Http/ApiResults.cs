using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FegHR.SharedKernel.Http;

/// <summary>Envelope-shaped results for minimal-API handlers.</summary>
public static class ApiResults
{
    public static IResult Ok<T>(T data, string? message = null) => Results.Ok(ApiResponse<T>.Ok(data, message));

    public static IResult Ok(string? message = null) => Results.Ok(ApiResponse.Ok(message));

    public static IResult Created<T>(string location, T data, string? message = null)
        => Results.Created(location, ApiResponse<T>.Ok(data, message));

    public static IResult Fail(string message, int statusCode = StatusCodes.Status400BadRequest,
        IDictionary<string, string[]>? errors = null)
        => Results.Json(ApiResponse.Fail(message, errors), ApiJson.Options, statusCode: statusCode);

    public static IResult NotFound(string what = "The record") => Fail($"{what} was not found.", StatusCodes.Status404NotFound);

    public static IResult Conflict(string message = "Someone else changed this record. Reload and try again.")
        => Fail(message, StatusCodes.Status409Conflict);
}

/// <summary>A business rule rejected the request (HTTP 422).</summary>
public sealed class BusinessRuleException(string message) : Exception(message);

/// <summary>The caller is signed in but lacks the right for this action (HTTP 403).</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>The requested record does not exist in the bound tenant (HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>
/// DataAnnotations validation for request records: <c>.Validate&lt;CreateItemRequest&gt;()</c>.
/// Failures return 400 with field errors in the envelope.
/// </summary>
public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder builder) where TRequest : class
        => builder.AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
            if (request is null)
                return ApiResults.Fail("The request body is missing.");

            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
                return await next(context);

            var errors = results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty])
                    .Select(m => (Member: ToCamel(m), Message: r.ErrorMessage ?? "Invalid value.")))
                .GroupBy(e => e.Member)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
            return ApiResults.Fail("Please correct the highlighted fields.", StatusCodes.Status400BadRequest, errors);
        });

    private static string ToCamel(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
