using System.Text.Json;
using System.Text.Json.Serialization;

namespace FegHR.SharedKernel.Http;

/// <summary>
/// Response envelope, identical in shape to DailyCommerce's so the Sakai UI
/// services and interceptors carry over unchanged.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public IDictionary<string, string[]>? Errors { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, IDictionary<string, string[]>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null)
        => new() { Success = true, Message = message };

    public new static ApiResponse Fail(string message, IDictionary<string, string[]>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}

/// <summary>One page of a list plus the total row count.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>Bound from the query string: <c>?page=1&amp;pageSize=20&amp;search=…</c>.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20, string? Search = null)
{
    public const int MaxPageSize = 200;
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 20 : Math.Min(PageSize, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

/// <summary>The wire JSON settings, shared by the host and by writes outside the endpoint pipeline.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
