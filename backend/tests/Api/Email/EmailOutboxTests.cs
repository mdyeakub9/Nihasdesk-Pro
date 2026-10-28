using System.Net.Mail;
using System.Text;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Email;

/// <summary>ADR 0013: transactional, encrypted, retried, delivered once.</summary>
[Collection(AuthTests.Name)]
public sealed class EmailOutboxTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    private OutboxProbe Outbox => new(Api);

    private static EmailMessage Message(string marker) =>
        new($"{marker}@example.com", $"Subject {marker}", $"Text with secret-token-{marker}", $"<p>secret-token-{marker}</p>");

    private int SentWith(string marker) => Api.Emails.Sent.Count(m => m.Subject == $"Subject {marker}");

    [Fact]
    public async Task Email_is_only_sent_when_the_transaction_commits()
    {
        var marker = Guid.NewGuid().ToString("N");

        await Outbox.EnqueueAndRollBackAsync(Message(marker));

        Assert.Equal(0, SentWith(marker));
    }

    [Fact]
    public async Task Queued_email_is_encrypted_at_rest_and_deleted_after_sending()
    {
        var marker = Guid.NewGuid().ToString("N");
        await Outbox.EnqueueAsync(Message(marker));

        var stored = (await Outbox.RowsAsync())[^1];
        var raw = Encoding.UTF8.GetString(stored.ProtectedMessage);
        Assert.DoesNotContain(marker, raw, StringComparison.Ordinal);

        Assert.Equal(1, SentWith(marker));
        Assert.DoesNotContain(await Outbox.RowsAsync(), r => r.Id == stored.Id);
    }

    [Fact]
    public async Task Failed_send_is_retried_after_a_backoff()
    {
        var marker = Guid.NewGuid().ToString("N");
        await Outbox.EnqueueAsync(Message(marker));
        Api.Emails.Fail = () => new SmtpException("test outage");
        try
        {
            await Api.DeliverEmailsAsync();
        }
        finally
        {
            Api.Emails.Fail = null;
        }

        var row = Assert.Single(await Outbox.RowsAsync(), r => r.LastError is not null && r.FailedAt is null);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(nameof(SmtpException), row.LastError);
        Assert.Equal(0, SentWith(marker)); // not due yet

        Api.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(1, SentWith(marker));
    }

    [Fact]
    public async Task Delivery_is_given_up_after_the_last_attempt_and_the_content_wiped()
    {
        var marker = Guid.NewGuid().ToString("N");
        await Outbox.EnqueueAsync(Message(marker));
        Api.Emails.Fail = () => new SmtpException("permanent");
        try
        {
            for (var i = 0; i < OutboxRetry.MaxAttempts; i++)
            {
                await Api.DeliverEmailsAsync();
                Api.Clock.Advance(TimeSpan.FromHours(13));
            }
        }
        finally
        {
            Api.Emails.Fail = null;
        }

        var failed = (await Outbox.RowsAsync()).Where(r => r.FailedAt is not null).OrderBy(r => r.CreatedAt).Last();
        Assert.Equal(OutboxRetry.MaxAttempts, failed.Attempts);
        Assert.Empty(failed.ProtectedMessage);
        Assert.Equal(0, SentWith(marker));
    }

    [Fact]
    public async Task Two_dispatchers_running_together_send_each_email_once()
    {
        var marker = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 30; i++)
        {
            await Outbox.EnqueueAsync(Message(marker));
        }

        await Outbox.DrainWithTwoDispatchersAsync();

        Assert.Equal(30, SentWith(marker));
    }
}
