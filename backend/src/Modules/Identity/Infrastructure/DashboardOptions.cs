namespace Nihasdesk.Modules.Identity.Infrastructure;

internal sealed class DashboardOptions
{
    public const string SectionName = "App";

    /// <summary>Base URL of the dashboard, used for links in emails (e.g. https://app.example.com).</summary>
    public string DashboardUrl { get; set; } = string.Empty;
}
