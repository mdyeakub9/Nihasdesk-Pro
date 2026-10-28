using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

internal enum AiTone
{
    Friendly,
    Formal,
}

/// <summary>
/// How the AI works for one workspace (PRD 4.8): on/off, tone, the owner's own guidance and the
/// disclosure text. One row per workspace; defaults apply until an owner saves.
/// </summary>
internal sealed class AiSettings : TenantEntity
{
    public const int InstructionsMaxLength = 2000;
    public const int DisclosureMaxLength = 300;

    private AiSettings(Guid tenantId)
        : base(tenantId)
    {
    }

    public bool Enabled { get; private set; } = true;

    public AiTone Tone { get; private set; } = AiTone.Friendly;

    /// <summary>The owner's guidance in plain language, e.g. "Always mention free shipping over €50".</summary>
    public string? Instructions { get; private set; }

    /// <summary>The line said before the first AI answer; null = the default (EU AI Act, ADR 0019).</summary>
    public string? Disclosure { get; private set; }

    public static AiSettings Defaults(Guid tenantId) => new(tenantId);

    public void Update(bool enabled, AiTone tone, string? instructions, string? disclosure)
    {
        Enabled = enabled;
        Tone = tone;
        Instructions = Clean(instructions, InstructionsMaxLength);
        Disclosure = Clean(disclosure, DisclosureMaxLength);
    }

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
