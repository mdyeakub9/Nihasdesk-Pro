using FluentValidation;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Notes;

/// <summary>A team note (PRD 4.4). <see cref="MentionedUserIds"/> must be members of this workspace.</summary>
internal sealed record AddNoteRequest(string Text, IReadOnlyList<Guid>? MentionedUserIds);

/// <summary>The side panel: pinned notes first, then the newest notes.</summary>
internal sealed record NotesResponse(IReadOnlyList<MessageResponse> Pinned, IReadOnlyList<MessageResponse> Recent);

internal static class NoteValues
{
    public const int MaxMentions = 10;
    public const int MaxPinned = 5;
    public const int RecentCount = 20;
}

internal sealed class AddNoteValidator : AbstractValidator<AddNoteRequest>
{
    public AddNoteValidator()
    {
        RuleFor(r => r.Text).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(Message.TextMaxLength);
        RuleFor(r => r.MentionedUserIds).Must(m => m!.Count <= NoteValues.MaxMentions).When(r => r.MentionedUserIds is not null)
            .WithMessage($"At most {NoteValues.MaxMentions} people can be mentioned.");
        RuleForEach(r => r.MentionedUserIds).NotEqual(Guid.Empty);
    }
}

internal static class NoteErrors
{
    /// <summary>Also for other conversations' and workspaces' notes, and for messages that aren't notes (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("note.not_found", "Note not found.");

    public static readonly Error MentionNotMember =
        Error.Validation("note.mention_not_member", "Only team members of this workspace can be mentioned.");

    public static readonly Error PinLimit =
        Error.Conflict("note.pin_limit", $"At most {NoteValues.MaxPinned} notes can be pinned. Unpin one first.");
}
