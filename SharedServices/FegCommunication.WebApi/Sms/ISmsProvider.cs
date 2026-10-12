// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Sms;

// Batch-aware send. Providers receive the full recipient list and decide
// whether to make a single campaign call (e.g. Alpha's comma-joined `to=`
// with a content_id) or fan out per-recipient. They MUST return exactly
// one result per input recipient so SmsService can attribute audit rows
// and produce a per-row response to the caller.
public interface ISmsProvider
{
    string Name { get; }

    Task<IReadOnlyList<SmsProviderResult>> SendAsync(
        IReadOnlyList<string> to, string message, string senderId, string? contentId,
        SmsBackendConfig? config, CancellationToken ct);
}

// Per-recipient outcome. ProviderMessageId is the gateway's request id;
// for a batched campaign send the same id is replicated across rows since
// the gateway returns a single id for the whole batch.
public sealed record SmsProviderResult(string To, bool Ok, string? Error, string? ProviderMessageId);
