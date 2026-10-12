// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using System.ComponentModel.DataAnnotations;

namespace FegCommunication.WebApi.Email;

// Attachment payload. Content is base64-encoded so JSON-only callers
// don't need multipart. ContentType is required so MailKit can stamp
// the right MIME header.
public sealed class EmailAttachment
{
    [Required, MaxLength(260)] public string FileName     { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string ContentType  { get; set; } = "application/octet-stream";
    [Required]                 public string ContentBase64 { get; set; } = string.Empty;
}

// /api/email/send body. To/Cc/Bcc are arrays so HR can target multiple
// stakeholders in one call. ReplyTo is optional. IsHtml controls
// whether the body is rendered as HTML or plain text.
public sealed class SendEmailRequest
{
    [Required, MinLength(1)] public List<string> To  { get; set; } = new();
                             public List<string> Cc  { get; set; } = new();
                             public List<string> Bcc { get; set; } = new();

    [MaxLength(120)]         public string? ReplyTo { get; set; }
    [Required, MaxLength(300)] public string Subject { get; set; } = string.Empty;
    [Required]               public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; } = true;

    public List<EmailAttachment> Attachments { get; set; } = new();

    // Optional override of the configured From identity -- useful when
    // a tenant wants notices to appear from "HR <hr@tenant.com>" while
    // payslips come from "Payroll <payroll@tenant.com>".
    [MaxLength(120)] public string? FromAddress { get; set; }
    [MaxLength(120)] public string? FromName    { get; set; }
}

// Per-recipient outcome. Mirrors the bulk-payslip-email shape from HRAccounts
// so callers can just iterate the list to render a status table.
public sealed class EmailDeliveryRow
{
    public string Address { get; set; } = string.Empty;
    public bool   Sent    { get; set; }
    public string? Error  { get; set; }
}

public sealed class SendEmailResult
{
    public int Total   { get; set; }
    public int Sent    { get; set; }
    public int Failed  { get; set; }
    public List<EmailDeliveryRow> Recipients { get; set; } = new();
}
