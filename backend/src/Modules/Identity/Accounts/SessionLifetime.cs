namespace Nihasdesk.Modules.Identity.Accounts;

/// <summary>Session lifetimes from ADR 0010.</summary>
internal static class SessionLifetime
{
    public static readonly TimeSpan BrowserIdle = TimeSpan.FromHours(2);

    public static readonly TimeSpan BrowserAbsolute = TimeSpan.FromHours(12);

    public static readonly TimeSpan RememberIdle = TimeSpan.FromDays(14);

    public static readonly TimeSpan RememberAbsolute = TimeSpan.FromDays(30);

    /// <summary>Last-seen updates are written at most this often, to keep reads cheap.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public static TimeSpan Idle(bool remember) => remember ? RememberIdle : BrowserIdle;

    public static TimeSpan Absolute(bool remember) => remember ? RememberAbsolute : BrowserAbsolute;
}
