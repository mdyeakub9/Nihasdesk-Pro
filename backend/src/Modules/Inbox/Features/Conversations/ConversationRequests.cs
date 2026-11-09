using System.Buffers.Text;
using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Conversations;

/// <summary>Query of <c>GET /api/v1/conversations</c>. Every value is allow-listed by the validator (SEC-01, SEC-21).</summary>
internal sealed record ListConversationsQuery(
    [FromQuery] string? Status,
    [FromQuery] string? Channel,
    [FromQuery] string? Responder,
    // "me", "unassigned" or a member's user ID.
    [FromQuery] string? Assignee,
    [FromQuery] bool? Unread,
    [FromQuery] string? Cursor,
    [FromQuery] int? Limit);

internal sealed record ListMessagesQuery([FromQuery] string? Before, [FromQuery] int? Limit);

internal sealed record SendMessageRequest(string Text);

/// <summary>Only the fields given are changed. Assignee has its own endpoint.</summary>
internal sealed record UpdateConversationRequest(string? Status, string? Responder, bool? AiEnabled, string? LeadStatus);

internal sealed record AssignRequest(Guid? UserId);

internal sealed record ConversationSummaryResponse(
    Guid Id,
    Guid ContactId,
    string? ContactName,
    string Channel,
    string Status,
    string Responder,
    bool AiEnabled,
    string LeadStatus,
    Guid? AssigneeId,
    bool Unread,
    string Preview,
    DateTimeOffset LastMessageAt);

internal sealed record ConversationPageResponse(IReadOnlyList<ConversationSummaryResponse> Items, string? NextCursor);

internal sealed record ConversationResponse(ConversationSummaryResponse Conversation, ContactSummary? Contact);

internal sealed record MessageResponse(
    Guid Id,
    string Author,
    Guid? AuthorUserId,
    string? AuthorName,
    string Text,
    bool Internal,
    DateTimeOffset SentAt,
    // none | pending | sent | failed (ADR 0017)
    string Delivery,
    // Notes only (PRD 4.4).
    IReadOnlyList<Guid> MentionedUserIds,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    // Product cards only (ADR 0024).
    ProductCardResponse? Product,
    // Photos, voice notes, video and files (ADR 0031).
    IReadOnlyList<AttachmentResponse> Attachments);

/// <summary>
/// Media of a message. <see cref="Url"/> is set once saved. Status: pending, saved, failed or too_large.
/// FailureCode: expired, type_not_allowed, too_large, network or source_not_allowed.
/// </summary>
internal sealed record AttachmentResponse(
    Guid Id,
    string Kind,
    string Status,
    string? FailureCode,
    string? ContentType,
    long? SizeBytes,
    int? Width,
    int? Height,
    string? FileName,
    string? Url,
    string? ThumbnailUrl);

/// <summary>A product as it was sent; price in cents.</summary>
internal sealed record ProductCardResponse(Guid ProductId, Guid VariantId, string Title, string Variant, string? Sku, long Price, string Currency);

/// <summary>Oldest first; <see cref="Before"/> loads the next older page.</summary>
internal sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, string? Before);

internal static class InboxValues
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;

    public static string Text<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>Exact lower-case names only; numbers and unknown names are rejected (SEC-21).</summary>
    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return value is not null
            && value.All(char.IsAsciiLetterLower)
            && Enum.GetNames<TEnum>().Any(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(value, ignoreCase: true, out result);
    }

    public static bool IsKnown<TEnum>(string? value)
        where TEnum : struct, Enum => TryParse<TEnum>(value, out _);
}

/// <summary>Opaque keyset cursor: a timestamp and an ID.</summary>
internal static class PageCursor
{
    public static string Encode(DateTimeOffset at, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{at.UtcTicks}.{id:N}")));

    public static bool TryDecode(string? cursor, out (DateTimeOffset At, Guid Id) value)
    {
        value = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 100)
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

internal sealed class ListConversationsValidator : AbstractValidator<ListConversationsQuery>
{
    public ListConversationsValidator()
    {
        RuleFor(q => q.Status).Must(InboxValues.IsKnown<ConversationStatus>).When(q => q.Status is not null)
            .WithMessage("Status must be waiting, open or done.");
        RuleFor(q => q.Channel).Must(c => ChannelKind.All.Contains(c!)).When(q => q.Channel is not null)
            .WithMessage("Unknown channel.");
        RuleFor(q => q.Responder).Must(InboxValues.IsKnown<Responder>).When(q => q.Responder is not null)
            .WithMessage("Responder must be ai or human.");
        RuleFor(q => q.Assignee).Must(a => a is "me" or "unassigned" || Guid.TryParse(a, out _)).When(q => q.Assignee is not null)
            .WithMessage("Assignee must be me, unassigned or a user ID.");
        RuleFor(q => q.Cursor).Must(c => PageCursor.TryDecode(c, out _)).When(q => q.Cursor is not null)
            .WithMessage("Invalid cursor.");
        RuleFor(q => q.Limit).InclusiveBetween(1, InboxValues.MaxPageSize);
    }
}

internal sealed class ListMessagesValidator : AbstractValidator<ListMessagesQuery>
{
    public ListMessagesValidator()
    {
        RuleFor(q => q.Before).Must(c => PageCursor.TryDecode(c, out _)).When(q => q.Before is not null)
            .WithMessage("Invalid cursor.");
        RuleFor(q => q.Limit).InclusiveBetween(1, InboxValues.MaxPageSize);
    }
}

internal sealed class SendMessageValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageValidator()
    {
        RuleFor(r => r.Text).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(Message.TextMaxLength);
    }
}

internal sealed class UpdateConversationValidator : AbstractValidator<UpdateConversationRequest>
{
    public UpdateConversationValidator()
    {
        RuleFor(r => r.Status).Must(InboxValues.IsKnown<ConversationStatus>).When(r => r.Status is not null)
            .WithMessage("Status must be waiting, open or done.");
        RuleFor(r => r.Responder).Must(InboxValues.IsKnown<Responder>).When(r => r.Responder is not null)
            .WithMessage("Responder must be ai or human.");
        RuleFor(r => r.LeadStatus).Must(InboxValues.IsKnown<LeadStatus>).When(r => r.LeadStatus is not null)
            .WithMessage("Lead status must be new, hot, customer, support or spam.");
    }
}

internal static class InboxErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("conversation.not_found", "Conversation not found.");

    public static readonly Error OutsideWindow = Error.Conflict(
        "conversation.outside_window",
        "The customer's last message is more than 24 hours old, so the channel doesn't allow a reply now.");

    public static readonly Error AssigneeNotMember =
        Error.Validation("conversation.assignee_not_member", "Conversations can be assigned to team members only.");
}
