// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using FegCommunication.WebApi.Logs;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MimeKit;
using Serilog;

namespace FegCommunication.WebApi.Email;

// Single-message multi-recipient send via MailKit. To/Cc/Bcc are all
// rolled into one MimeMessage with the right header buckets so the
// recipients see exactly what HR composed (cc-ed people see the cc
// list; bcc-ed people don't appear to anyone). Per-recipient status
// rows are filled in by attempting one transactional send -- if SMTP
// rejects the whole batch, every row is marked failed with the same
// reason so the caller can decide whether to retry.
//
// **Best-effort**: never throws. SMTP-disabled / unreachable returns a
// SendEmailResult with Sent=0 and a per-recipient Error string. This
// matches HRAccounts's existing payslip-email contract.
internal sealed class EmailService(
    IOptions<SmtpSettings> opts,
    ICommunicationLogStore store,
    IHttpContextAccessor http) : IEmailService
{
    private readonly SmtpSettings _s = opts.Value;

    public async Task<SendEmailResult> SendAsync(SendEmailRequest req, CancellationToken ct = default)
    {
        var addresses = req.To.Concat(req.Cc).Concat(req.Bcc)
                            .Where(a => !string.IsNullOrWhiteSpace(a))
                            .Select(a => a.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        var result = new SendEmailResult { Total = addresses.Count };
        result.Recipients = addresses.Select(a => new EmailDeliveryRow { Address = a }).ToList();

        if (result.Total == 0)
        {
            result.Failed = 0;
            return result;
        }

        var caller = http.HttpContext?.User?.Identity?.Name;
        var fromForLog = string.IsNullOrWhiteSpace(req.FromAddress) ? _s.FromAddress : req.FromAddress;

        if (!_s.Enabled || string.IsNullOrWhiteSpace(_s.Host))
        {
            const string reason = "SMTP disabled or Host not configured";
            foreach (var r in result.Recipients) r.Error = reason;
            result.Failed = result.Total;
            foreach (var r in result.Recipients)
                await store.LogEmailAsync(r.Address, req.Subject, fromForLog, sent: false, error: reason, caller, ct);
            return result;
        }

        try
        {
            var msg = BuildMessage(req);

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _s.Host, _s.Port,
                _s.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct);
            if (!string.IsNullOrWhiteSpace(_s.Username))
                await client.AuthenticateAsync(_s.Username, _s.Password ?? string.Empty, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);

            foreach (var r in result.Recipients) r.Sent = true;
            result.Sent = result.Total;
            foreach (var r in result.Recipients)
                await store.LogEmailAsync(r.Address, req.Subject, fromForLog, sent: true, error: null, caller, ct);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SMTP send failed for {Subject} to {Count} recipients", req.Subject, result.Total);
            foreach (var r in result.Recipients) r.Error = ex.Message;
            result.Failed = result.Total;
            foreach (var r in result.Recipients)
                await store.LogEmailAsync(r.Address, req.Subject, fromForLog, sent: false, error: ex.Message, caller, ct);
            return result;
        }
    }

    private MimeMessage BuildMessage(SendEmailRequest req)
    {
        var msg = new MimeMessage();
        var fromAddr = string.IsNullOrWhiteSpace(req.FromAddress) ? _s.FromAddress : req.FromAddress;
        var fromName = string.IsNullOrWhiteSpace(req.FromName)    ? _s.FromName    : req.FromName;
        msg.From.Add(new MailboxAddress(fromName, fromAddr));

        foreach (var to  in req.To.Where(a => !string.IsNullOrWhiteSpace(a)))  msg.To.Add(MailboxAddress.Parse(to.Trim()));
        foreach (var cc  in req.Cc.Where(a => !string.IsNullOrWhiteSpace(a)))  msg.Cc.Add(MailboxAddress.Parse(cc.Trim()));
        foreach (var bcc in req.Bcc.Where(a => !string.IsNullOrWhiteSpace(a))) msg.Bcc.Add(MailboxAddress.Parse(bcc.Trim()));

        if (!string.IsNullOrWhiteSpace(req.ReplyTo))
            msg.ReplyTo.Add(MailboxAddress.Parse(req.ReplyTo.Trim()));

        msg.Subject = req.Subject;

        var bb = new BodyBuilder();
        if (req.IsHtml) bb.HtmlBody = req.Body;
        else            bb.TextBody = req.Body;

        foreach (var att in req.Attachments)
        {
            if (string.IsNullOrWhiteSpace(att.ContentBase64)) continue;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(att.ContentBase64); }
            catch (FormatException) { throw new ArgumentException($"Attachment '{att.FileName}' has invalid base64 content"); }
            bb.Attachments.Add(att.FileName, bytes, ContentType.Parse(att.ContentType));
        }

        msg.Body = bb.ToMessageBody();
        return msg;
    }
}
