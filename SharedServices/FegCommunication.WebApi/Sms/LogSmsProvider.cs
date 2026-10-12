// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using Serilog;

namespace FegCommunication.WebApi.Sms;

// Default provider for dev boxes -- writes the SMS to Serilog and
// returns success with a synthetic messageId. Operations teams can
// confirm the wiring works end-to-end without paying for actual SMS.
internal sealed class LogSmsProvider : ISmsProvider
{
    public string Name => "Log";

    public Task<IReadOnlyList<SmsProviderResult>> SendAsync(
        IReadOnlyList<string> to, string message, string senderId, string? contentId,
        SmsBackendConfig? config, CancellationToken ct)
    {
        // LogSmsProvider has no credentials of its own, so config is ignored.
        // We still emit one log line per recipient so dev traces match what
        // a real provider would produce on a campaign send.
        var results = new List<SmsProviderResult>(to.Count);
        foreach (var number in to)
        {
            var id = $"log-{Guid.NewGuid():N}";
            Log.Information("[SMS-LOG] from={Sender} to={To} body={Body} contentId={ContentId} (id={Id})",
                senderId, number, message, contentId, id);
            results.Add(new SmsProviderResult(number, true, null, id));
        }
        return Task.FromResult<IReadOnlyList<SmsProviderResult>>(results);
    }
}
