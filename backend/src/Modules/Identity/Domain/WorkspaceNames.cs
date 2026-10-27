namespace Nihasdesk.Modules.Identity.Domain;

internal static class WorkspaceNames
{
    /// <summary>Trims and checks a display name; request validation gives friendlier errors before this.</summary>
    public static string Validate(string name, int maxLength)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Name must be 1–{maxLength} characters.", nameof(name));
        }

        return trimmed;
    }
}
