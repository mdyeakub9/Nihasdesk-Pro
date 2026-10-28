using Microsoft.Extensions.Options;

namespace Nihasdesk.Modules.Identity.Infrastructure;

/// <summary>Links to dashboard pages used in auth emails.</summary>
internal sealed class DashboardLinks(IOptions<DashboardOptions> options)
{
    public string Login => Build("/login");

    public string VerifyEmail(Guid userId, string token) => Build($"/verify-email?user={userId}&token={Uri.EscapeDataString(token)}");

    public string ResetPassword(Guid userId, string token) => Build($"/reset-password?user={userId}&token={Uri.EscapeDataString(token)}");

    private string Build(string path) => options.Value.DashboardUrl.TrimEnd('/') + path;
}
