using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Channels.Infrastructure.Meta;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.TestSupport;

/// <summary>The real API on a real database, with captured email and a controllable clock.</summary>
/// <param name="useRealAi">Keeps the configured AI provider instead of <see cref="Ai"/>; only the eval runner sets it.</param>
public sealed class NihasdeskApiFactory(PostgresDatabase database, IReadOnlyDictionary<string, string?>? settings = null, bool useRealAi = false)
    : WebApplicationFactory<Program>
{
    public const string DashboardUrl = "https://app.test";

    public FakeEmailSender Emails { get; } = new();

    public TestClock Clock { get; } = new();

    public FakeHibpHandler Hibp { get; } = new();

    public FakeMetaGraphHandler Meta { get; } = new();

    public FakeAi Ai { get; } = new();

    public const string MetaVerifyToken = "tests-only-verify-token";

    internal FakeEmbeddings Embeddings { get; } = new();

    /// <summary>Web pages a source may fetch: no test reaches the real internet.</summary>
    public FakeWebHandler Web { get; } = new();

    /// <summary>Reads waiting sources like the Workers job, until none is due now.</summary>
    public async Task ReadSourcesAsync()
    {
        var processor = Services.GetRequiredService<SourceReadProcessor>();
        while (await processor.ProcessOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Finds waiting sites' pages like the Workers job, until none is due now.</summary>
    public async Task FindSitesAsync()
    {
        var processor = Services.GetRequiredService<SiteFindProcessor>();
        while (await processor.ProcessOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Downloads waiting media like the Workers job, until none is due now.</summary>
    public async Task DownloadMediaAsync()
    {
        var processor = Services.GetRequiredService<MediaDownloadProcessor>();
        while (await processor.ProcessOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Runs the daily media retention job once; returns how many attachments it removed.</summary>
    public Task<int> PurgeOldMediaAsync() =>
        Services.GetRequiredService<MediaRetention>().PurgeOnceAsync(CancellationToken.None);

    /// <summary>Where Meta would fetch product images from.</summary>
    public const string PublicUrl = "https://public.nihasdesk.test";

    public const string ImageSigningKey = "dGVzdC1vbmx5LXByb2R1Y3QtaW1hZ2Utc2lnbmluZy1rZXktMzJieXRlcw==";

    /// <summary>Uploaded files of this test run; nothing lands in the developer's own folder.</summary>
    public string FilesFolder { get; } = Path.Combine(Path.GetTempPath(), "nihasdesk-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        Emails.Deliver = DeliverEmailsAsync;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.AppConnectionName}", database.AppConnectionString);
        builder.UseSetting("App:DashboardUrl", DashboardUrl);
        builder.UseSetting("Meta:AppId", "tests-app");
        builder.UseSetting("Meta:AppSecret", FakeMetaGraphHandler.AppSecret);
        builder.UseSetting("Meta:WebhookVerifyToken", MetaVerifyToken);
        // Tests run the webhook processor and the sender step by step.
        builder.UseSetting("Channels:BackgroundProcessing", "false");
        builder.UseSetting("Ai:BackgroundProcessing", "false");
        builder.UseSetting("Ai:SiteRequestDelay", "00:00:00");
        builder.UseSetting("Storage:LocalPath", FilesFolder);
        // Signed product image links (ADR 0031 §7); a test-only key.
        builder.UseSetting("App:PublicUrl", PublicUrl);
        builder.UseSetting("Commerce:Images:SigningKey", ImageSigningKey);
        // Low enough for the limit test to reach.
        builder.UseSetting("Ai:MaxRepliesPerConversationPerHour", "3");
        builder.UseSetting("Ai:MaxSuggestionsPerWorkspacePerDay", "4");
        // Generous by default; rate-limit tests pass their own limits.
        foreach (var policy in new[] { "login", "signup", "email", "invite", "messages", "uploads", "product-images", "public-media" })
        {
            builder.UseSetting($"RateLimits:{policy}:PermitLimit", "1000");
            builder.UseSetting($"RateLimits:{policy}:Window", "00:01:00");
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            // The API only queues email; tests deliver the outbox on demand instead of a Workers host.
            services.AddOptions<EmailOutboxOptions>();
            services.AddSingleton<EmailOutboxDispatcher<IdentityModuleDbContext>>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient("hibp").ConfigurePrimaryHttpMessageHandler(() => Hibp);
            services.AddHttpClient(MetaGraphClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Meta);
            if (!useRealAi)
            {
                services.RemoveAll<ILlmProvider>();
                services.AddSingleton(Ai.Provider);
                services.RemoveAll<IEmbeddingProvider>();
                services.AddSingleton<IEmbeddingProvider>(Embeddings);
            }

            services.RemoveAll<SafeHttpClient>();
            services.AddSingleton(_ => new SafeHttpClient(
                Web,
                (host, _) => Task.FromResult(new[]
                {
                    host == "localhost" || host.StartsWith("private.", StringComparison.Ordinal)
                        ? System.Net.IPAddress.Loopback
                        : System.Net.IPAddress.Parse("93.184.216.34"),
                })));
        });
    }

    /// <summary>Delivers everything due in the outbox, like the Workers host would.</summary>
    public async Task DeliverEmailsAsync()
    {
        var dispatcher = Services.GetRequiredService<EmailOutboxDispatcher<IdentityModuleDbContext>>();
        while (await dispatcher.DispatchOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Handles stored Meta webhooks like the background processor would.</summary>
    public async Task ProcessMetaWebhooksAsync()
    {
        var processor = Services.GetRequiredService<MessengerWebhookProcessor>();
        while (await processor.ProcessOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Lets the AI answer due conversations, after the short wait for more customer messages.</summary>
    public async Task<int> RunAiAsync()
    {
        Clock.Advance(TimeSpan.FromSeconds(5));
        return await Services.GetRequiredService<AiReplyProcessor>().ProcessOnceAsync(CancellationToken.None);
    }

    /// <summary>Updates due summaries like the Workers job.</summary>
    public async Task RunSummariesAsync()
    {
        var processor = Services.GetRequiredService<ConversationSummaryProcessor>();
        while (await processor.ProcessOnceAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Sends due team replies like the background sender would.</summary>
    public Task<int> SendRepliesAsync() => Services.GetRequiredService<MessengerSender>().SendOnceAsync(CancellationToken.None);

    /// <summary>A browser-like client: HTTPS (the session cookie is Secure) with its own cookie jar.</summary>
    public ApiClient CreateApiClient(bool followRedirects = true) =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = followRedirects }));
}

/// <summary>Captures sent email. Reading <see cref="Sent"/> first delivers the outbox.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Set by the factory: delivers due outbox emails before tests look at them.</summary>
    internal Func<Task>? Deliver { get; set; }

    /// <summary>When set, sending throws this, like an SMTP outage.</summary>
    public Func<Exception>? Fail { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            Deliver?.Invoke().GetAwaiter().GetResult();
            return [.. _sent];
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail is { } fail)
        {
            throw fail();
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage LastTo(string address) => Sent.Last(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase));
}

/// <summary>System time plus an offset that tests can move forward.</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}
