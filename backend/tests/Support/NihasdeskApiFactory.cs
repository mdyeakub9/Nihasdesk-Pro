using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Infrastructure.Persistence;

namespace Nihasdesk.TestSupport;

/// <summary>The real API on a real database, with captured email and a controllable clock.</summary>
public sealed class NihasdeskApiFactory(PostgresDatabase database, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string DashboardUrl = "https://app.test";

    public FakeEmailSender Emails { get; } = new();

    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.AppConnectionName}", database.AppConnectionString);
        builder.UseSetting("App:DashboardUrl", DashboardUrl);
        // Generous by default; rate-limit tests pass their own limits.
        foreach (var policy in new[] { "login", "signup", "email" })
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
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>A browser-like client: HTTPS (the session cookie is Secure) with its own cookie jar.</summary>
    public ApiClient CreateApiClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }));
}

public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
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
