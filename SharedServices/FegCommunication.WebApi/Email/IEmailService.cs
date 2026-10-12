// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Email;

public interface IEmailService
{
    Task<SendEmailResult> SendAsync(SendEmailRequest req, CancellationToken ct = default);
}
