using System.Text.Encodings.Web;
using Nihasdesk.Infrastructure.Email;

namespace Nihasdesk.Modules.Identity.Infrastructure.Email;

/// <summary>Auth emails in Italian and English (PRD 4.13). Every dynamic value is HTML-encoded (SEC-02).</summary>
internal static class AuthEmails
{
    public static EmailMessage VerifyEmail(string to, string language, string name, string link)
    {
        var it = IsItalian(language);
        var subject = it ? "Conferma il tuo indirizzo email" : "Confirm your email address";
        var greeting = it ? $"Ciao {name}," : $"Hi {name},";
        var body = it
            ? "conferma il tuo indirizzo email per attivare il tuo account Nihasdesk. Il link scade tra un'ora."
            : "please confirm your email address to activate your Nihasdesk account. The link expires in one hour.";
        var action = it ? "Conferma email" : "Confirm email";
        var ignore = it
            ? "Se non hai creato tu l'account, ignora questa email."
            : "If you didn't create this account, you can ignore this email.";

        return Render(to, subject, greeting, body, action, link, ignore);
    }

    /// <summary>Sent instead of a second account, so signup never reveals whether an email is registered (SEC-26).</summary>
    public static EmailMessage AccountExists(string to, string language, string loginLink)
    {
        var it = IsItalian(language);
        var subject = it ? "Hai già un account Nihasdesk" : "You already have a Nihasdesk account";
        var greeting = it ? "Ciao," : "Hi,";
        var body = it
            ? "qualcuno ha provato a registrarsi con questo indirizzo email, ma esiste già un account. Puoi accedere qui."
            : "someone tried to sign up with this email address, but an account already exists. You can sign in here.";
        var action = it ? "Accedi" : "Sign in";
        var ignore = it
            ? "Se non sei stato tu, ignora questa email: il tuo account non è cambiato."
            : "If this wasn't you, ignore this email: nothing has changed on your account.";

        return Render(to, subject, greeting, body, action, loginLink, ignore);
    }

    private static bool IsItalian(string language) => language.StartsWith("it", StringComparison.OrdinalIgnoreCase);

    private static EmailMessage Render(string to, string subject, string greeting, string body, string action, string link, string footer)
    {
        var text = $"{greeting}\n\n{body}\n\n{action}: {link}\n\n{footer}\n";
        var html = $"""
            <!doctype html>
            <html><body style="font-family:Inter,Arial,sans-serif;color:#0f172a;line-height:1.5">
            <p>{Encode(greeting)}</p>
            <p>{Encode(body)}</p>
            <p><a href="{Encode(link)}" style="display:inline-block;background:#a3e635;color:#0f172a;padding:10px 16px;border-radius:8px;text-decoration:none;font-weight:600">{Encode(action)}</a></p>
            <p style="color:#64748b;font-size:13px">{Encode(footer)}</p>
            </body></html>
            """;

        return new EmailMessage(to, subject, text, html);
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
