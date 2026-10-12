// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Sms;

public interface ISmsService
{
    IReadOnlyCollection<string> KnownProviders { get; }
    Task<SendSmsResult> SendAsync(SendSmsRequest req, CancellationToken ct = default);
}
