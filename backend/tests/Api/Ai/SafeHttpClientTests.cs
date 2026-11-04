using System.Net;
using System.Text;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Storage;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>SEC-05: what a user-given URL may reach. SEC-04: file keys stay inside the storage folder.</summary>
public sealed class SafeHttpClientTests
{
    private static readonly Uri Page = new("https://shop.example/page");

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2002:7f00:1::")]
    public void Addresses_that_are_not_public_are_blocked(string address) =>
        Assert.True(SafeHttpClient.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.False(SafeHttpClient.IsBlocked(IPAddress.Parse(address)));

    [Fact]
    public async Task A_name_with_any_private_address_is_refused()
    {
        using var client = Client(_ => Text("no"), "93.184.216.34", "10.0.0.1");

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.CheckAsync(Page, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.BlockedAddress, error.Failure);
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused_before_it_is_followed()
    {
        var requested = new List<string>();
        using var client = Client(request =>
        {
            requested.Add(request.RequestUri!.Host);
            return Redirect("http://169.254.169.254/latest/meta-data/");
        });

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.GetAsync(Page, 1000, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.BlockedAddress, error.Failure);
        Assert.Equal(["shop.example"], requested);
    }

    [Fact]
    public async Task At_most_three_redirects_are_followed()
    {
        var hops = 0;
        using var client = Client(request =>
        {
            hops++;
            return Redirect($"https://shop.example/{hops}");
        });

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.GetAsync(Page, 1000, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.TooManyRedirects, error.Failure);
        Assert.Equal(SafeHttpClient.MaxRedirects + 1, hops);
    }

    [Fact]
    public async Task Redirects_within_the_limit_are_followed_and_the_final_page_is_returned()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/page" ? Redirect("/final") : Text("Ciao"));

        var page = await client.GetAsync(Page, 1000, CancellationToken.None);

        Assert.Equal("https://shop.example/final", page.FinalUrl.AbsoluteUri);
        Assert.Equal("Ciao", Encoding.UTF8.GetString(page.Body));
        Assert.Equal("text/plain", page.ContentType);
    }

    [Fact]
    public async Task A_page_over_the_limit_is_refused_even_without_a_length_header()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[5000])) });

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.GetAsync(Page, 1000, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.TooLarge, error.Failure);
    }

    [Fact]
    public async Task Error_statuses_fail()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.GetAsync(Page, 1000, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.BadStatus, error.Failure);
    }

    [Fact]
    public async Task The_real_handler_checks_again_at_connect_time_against_dns_rebinding()
    {
        // The first lookup says public; when connecting, "localhost" is really a loopback address.
        using var client = new SafeHttpClient(null, (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var error = await Assert.ThrowsAsync<SafeHttpException>(() => client.GetAsync(new Uri("http://localhost/"), 1000, CancellationToken.None));

        Assert.Equal(SafeHttpFailure.BlockedAddress, error.Failure);
    }

    [Theory]
    [InlineData("../../windows/system32/config")]
    [InlineData("0123456789abcdef0123456789abcdef/../../secret")]
    [InlineData("C:/secret")]
    [InlineData("")]
    public async Task File_keys_that_could_leave_the_folder_are_refused(string key)
    {
        var store = new LocalFileStore(Path.Combine(Path.GetTempPath(), "nihasdesk-tests", Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Files_are_kept_under_a_random_key_per_workspace()
    {
        var store = new LocalFileStore(Path.Combine(Path.GetTempPath(), "nihasdesk-tests", Guid.NewGuid().ToString("N")));
        var tenant = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("ciao"));

        var key = await store.SaveAsync(tenant, content, CancellationToken.None);

        Assert.StartsWith(tenant.ToString("N") + "/", key, StringComparison.Ordinal);
        await using (var read = await store.OpenReadAsync(key, CancellationToken.None))
        using (var reader = new StreamReader(read))
        {
            Assert.Equal("ciao", await reader.ReadToEndAsync());
        }

        await store.DeleteAsync(key, CancellationToken.None);
        await store.DeleteAsync(key, CancellationToken.None);
    }

    private static SafeHttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, params string[] addresses) =>
        new(new FakeHandler(respond), (_, _) => Task.FromResult((addresses.Length == 0 ? ["93.184.216.34"] : addresses).Select(IPAddress.Parse).ToArray()));

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static HttpResponseMessage Text(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
