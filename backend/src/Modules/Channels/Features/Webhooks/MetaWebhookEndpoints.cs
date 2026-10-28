using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Channels.Infrastructure.Meta;
using Nihasdesk.Modules.Channels.Infrastructure.Webhooks;

namespace Nihasdesk.Modules.Channels.Features.Webhooks;

/// <summary>
/// <c>/api/v1/webhooks/meta</c>: called by Meta, not by browsers, so no session and no CSRF token.
/// Every delivery is authenticated by its signature instead (SEC-17); unsigned or wrongly signed
/// requests are rejected before anything is stored.
/// </summary>
internal static partial class MetaWebhookEndpoints
{
    public const string Path = "/api/v1/webhooks/meta";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var webhook = endpoints.MapGroup(Path).AllowAnonymous().DisableAntiforgery();

        // Meta's one-time check when the webhook URL is saved in the app dashboard.
        webhook.MapGet("/", (HttpRequest request, IOptions<MetaOptions> options) =>
        {
            var query = request.Query;
            var challenge = query["hub.challenge"].ToString();
            return query["hub.mode"] == "subscribe"
                && MetaCrypto.FixedTimeEquals(query["hub.verify_token"].ToString(), options.Value.WebhookVerifyToken)
                && Challenge().IsMatch(challenge)
                    ? Results.Text(challenge, "text/plain")
                    : Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        webhook.MapPost("/", async (
            HttpRequest request,
            IOptions<MetaOptions> options,
            ChannelsDbContext db,
            IDataProtectionProvider dataProtection,
            TimeProvider time,
            CancellationToken ct) =>
        {
            if (!options.Value.IsConfigured)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            var body = buffer.ToArray();
            if (!MetaCrypto.IsValidSignature(body, request.Headers["X-Hub-Signature-256"].ToString(), options.Value.AppSecret))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var now = time.GetUtcNow();
            db.WebhookEvents.Add(new WebhookEvent
            {
                Provider = "meta",
                ProtectedPayload = WebhookPayload.Protector(dataProtection).Protect(body),
                ReceivedAt = now,
                NextAttemptAt = now,
            });
            await db.SaveChangesAsync(ct);

            // Answer fast; the processor does the work (Meta retries slow or failed deliveries).
            return Results.Ok();
        });
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    private static partial Regex Challenge();
}

internal static class WebhookPayload
{
    public static IDataProtector Protector(IDataProtectionProvider provider) =>
        provider.CreateProtector("Nihasdesk.Channels.WebhookPayload.v1");
}
