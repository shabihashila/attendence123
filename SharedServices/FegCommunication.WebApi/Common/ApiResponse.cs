// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Common;

// Same shape as HRAccounts.Application.Dtos.Common.ApiResponse so callers
// (HRAccounts and any other consumer) parse exactly one envelope across both
// services. Self-contained on purpose -- this project does not reference
// HRAccounts's Application layer.
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
