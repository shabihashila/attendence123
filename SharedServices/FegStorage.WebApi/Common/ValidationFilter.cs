// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using System.ComponentModel.DataAnnotations;

namespace FegStorage.WebApi.Common;

// Endpoint filter that runs DataAnnotations validation on a bound body
// argument and short-circuits with the ApiResponse error envelope when
// it fails — same shape the MVC controllers used so callers see one
// validation response across HRAccounts + both shared services.
public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder ValidateBody<T>(this RouteHandlerBuilder builder) where T : class
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            foreach (var arg in ctx.Arguments)
            {
                if (arg is T model)
                {
                    var results = new List<ValidationResult>();
                    var vctx = new ValidationContext(model);
                    if (!Validator.TryValidateObject(model, vctx, results, validateAllProperties: true))
                    {
                        var errors = new Dictionary<string, string[]>();
                        foreach (var r in results)
                        {
                            var key = r.MemberNames.FirstOrDefault() ?? string.Empty;
                            var msg = r.ErrorMessage ?? string.Empty;
                            if (errors.TryGetValue(key, out var existing))
                                errors[key] = existing.Append(msg).ToArray();
                            else
                                errors[key] = new[] { msg };
                        }
                        return Results.BadRequest(ApiResponse.Fail("Validation failed", errors));
                    }
                }
            }
            return await next(ctx);
        });
        return builder;
    }
}
