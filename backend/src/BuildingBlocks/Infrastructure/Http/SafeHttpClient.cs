using System.Net;
using System.Net.Sockets;

namespace Nihasdesk.Infrastructure.Http;

/// <summary>Why a fetch was refused or failed; codes the caller turns into plain words.</summary>
public enum SafeHttpFailure
{
    /// <summary>Not an absolute http(s) URL on port 80/443, or it carries a user name or password.</summary>
    InvalidUrl,

    /// <summary>The address is private, loopback, link-local, multicast, metadata or otherwise not public.</summary>
    BlockedAddress,

    /// <summary>The host name doesn't resolve, or the connection failed.</summary>
    Unreachable,

    TooManyRedirects,

    TooLarge,

    Timeout,

    /// <summary>The server answered with an error status.</summary>
    BadStatus,
}

public sealed class SafeHttpException(SafeHttpFailure failure, string message) : Exception(message)
{
    public SafeHttpFailure Failure { get; } = failure;
}

public sealed record SafeHttpResponse(Uri FinalUrl, string? ContentType, byte[] Body);

/// <summary>
/// The one way to fetch a URL a user gave (SEC-05): http(s) on ports 80/443 only, no private or
/// metadata addresses (checked before every hop and again at connect time, against DNS rebinding),
/// at most 3 redirects, timeouts, a size limit, and no cookies or auth headers of ours.
/// </summary>
public sealed class SafeHttpClient : IDisposable
{
    public const int MaxRedirects = 3;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    private static readonly IPNetwork[] BlockedNetworks =
    [
        // IPv4: "this network", private, shared, loopback, link-local + metadata, special, test, benchmark, multicast, reserved.
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),

        // IPv6: unspecified, loopback, NAT64 and 6to4 (they embed IPv4), documentation, unique local, link-local, multicast.
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    private readonly HttpClient _http;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public SafeHttpClient()
        : this(null, null)
    {
    }

    /// <summary>Tests pass their own handler and name resolver; production uses the guarded socket handler.</summary>
    public SafeHttpClient(HttpMessageHandler? handler, Func<string, CancellationToken, Task<IPAddress[]>>? resolve)
    {
        _resolve = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
        _http = new HttpClient(handler ?? GuardedHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NihasdeskBot/1.0");
    }

    /// <summary>True for every address that isn't a normal public one. IPv4-mapped IPv6 counts as its IPv4 address.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return BlockedNetworks.Any(n => n.Contains(plain));
    }

    /// <summary>Checks a URL without fetching it; throws <see cref="SafeHttpException"/> when it may not be fetched.</summary>
    public async Task CheckAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        CheckShape(url);
        await CheckAddressesAsync(url.IdnHost, ct);
    }

    /// <summary>GET with redirects followed by hand, so each hop is checked. Reads at most <paramref name="maxBytes"/>.</summary>
    public async Task<SafeHttpResponse> GetAsync(Uri url, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TotalTimeout);
        try
        {
            var current = url;
            for (var hop = 0; ; hop++)
            {
                await CheckAsync(current, timeout.Token);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects || response.Headers.Location is not { } location)
                    {
                        throw new SafeHttpException(SafeHttpFailure.TooManyRedirects, "Too many redirects.");
                    }

                    current = new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new SafeHttpException(SafeHttpFailure.BadStatus, $"The server answered {(int)response.StatusCode}.");
                }

                var body = await ReadLimitedAsync(response.Content, maxBytes, timeout.Token);
                return new SafeHttpResponse(current, response.Content.Headers.ContentType?.MediaType, body);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SafeHttpException(SafeHttpFailure.Timeout, "The server took too long.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SafeHttpException inner)
        {
            // Refused at connect time: the name changed to a blocked address after the check.
            throw inner;
        }
        catch (HttpRequestException)
        {
            throw new SafeHttpException(SafeHttpFailure.Unreachable, "The server can't be reached.");
        }
    }

    public void Dispose() => _http.Dispose();

    private static void CheckShape(Uri url)
    {
        if (!url.IsAbsoluteUri
            || url.Scheme is not ("http" or "https")
            || !url.IsDefaultPort && url.Port is not (80 or 443)
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new SafeHttpException(SafeHttpFailure.InvalidUrl, "Only http(s) addresses on the standard ports.");
        }
    }

    private async Task CheckAddressesAsync(string host, CancellationToken ct) =>
        Guard(await ResolveAsync(_resolve, host, ct));

    private static async Task<IPAddress[]> ResolveAsync(Func<string, CancellationToken, Task<IPAddress[]>> resolve, string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return [literal];
        }

        try
        {
            return await resolve(host, ct);
        }
        catch (SocketException)
        {
            throw new SafeHttpException(SafeHttpFailure.Unreachable, "The address can't be found.");
        }
    }

    /// <summary>One blocked address refuses the host: a name with a public and a private address is not safe.</summary>
    private static IPAddress[] Guard(IPAddress[] addresses)
    {
        if (addresses.Length == 0)
        {
            throw new SafeHttpException(SafeHttpFailure.Unreachable, "The address can't be found.");
        }

        if (addresses.Any(IsBlocked))
        {
            throw new SafeHttpException(SafeHttpFailure.BlockedAddress, "This address isn't public.");
        }

        return addresses;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new SafeHttpException(SafeHttpFailure.TooLarge, "The page is too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new SafeHttpException(SafeHttpFailure.TooLarge, "The page is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Connects only to the checked address, so a name can't change to a private one in between (rebinding).</summary>
    private static SocketsHttpHandler GuardedHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        ConnectTimeout = ConnectTimeout,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = Guard(await ResolveAsync((host, c) => Dns.GetHostAddressesAsync(host, c), context.DnsEndPoint.Host, ct));
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
