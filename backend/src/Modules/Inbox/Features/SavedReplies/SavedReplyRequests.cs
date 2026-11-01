using System.Text.RegularExpressions;
using FluentValidation;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.SavedReplies;

/// <summary>Create or replace a saved reply. At least one of the texts is required.</summary>
internal sealed record SavedReplyRequest(string? Shortcut, string? Title, string? TextEn, string? TextIt);

internal sealed record SavedReplyResponse(Guid Id, string Shortcut, string Title, string? TextEn, string? TextIt, DateTimeOffset UpdatedAt);

internal sealed partial class SavedReplyValidator : AbstractValidator<SavedReplyRequest>
{
    public SavedReplyValidator()
    {
        RuleFor(r => r.Shortcut).NotEmpty().MaximumLength(SavedReply.ShortcutMaxLength)
            .Must(s => s is not null && Shortcut().IsMatch(s.Trim().ToLowerInvariant()))
            .WithMessage("Shortcut must be letters, digits or dashes, e.g. ship or size-guide.");
        RuleFor(r => r.Title).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(SavedReply.TitleMaxLength);
        RuleFor(r => r.TextEn).MaximumLength(SavedReply.TextMaxLength);
        RuleFor(r => r.TextIt).MaximumLength(SavedReply.TextMaxLength);
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.TextEn) || !string.IsNullOrWhiteSpace(r.TextIt))
            .WithName("text")
            .WithMessage("Write the English or the Italian text.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex Shortcut();
}

internal static class SavedReplyErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("saved_reply.not_found", "Saved reply not found.");

    public static readonly Error ShortcutTaken =
        Error.Conflict("saved_reply.shortcut_taken", "Another saved reply already uses this shortcut.");

    public static readonly Error Limit =
        Error.Conflict("saved_reply.limit", $"A workspace can have at most {SavedReply.MaxPerWorkspace} saved replies.");
}
