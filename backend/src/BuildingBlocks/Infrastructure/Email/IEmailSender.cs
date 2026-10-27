namespace Nihasdesk.Infrastructure.Email;

/// <summary>One email, already rendered. HTML bodies must HTML-encode every dynamic value (SEC-02).</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The only way modules send email (ADR 0008). Production: Brevo SMTP relay; local: Mailpit.
/// Implementations never log addresses or bodies (SEC-19).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
