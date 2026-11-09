using System.Buffers;
using System.Security.Cryptography;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// One button or quick reply of a sent rich message (ADR 0032 §3). The channel payload carries only
/// <c>rm1:&lt;token&gt;</c>; everything a tap acts on lives here, in the workspace that sent it, so a
/// forged, old or copied payload can never reach another conversation, product or workspace (SEC-36).
/// </summary>
internal sealed class MessageAction : TenantEntity
{
    public const string PayloadPrefix = "rm1:";
    public const int TokenLength = 22;
    public const int ActionMaxLength = 30;
    public const int LabelMaxLength = 40;
    public const int ValueMaxLength = 100;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private static readonly SearchValues<char> TokenChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    private MessageAction(Guid tenantId, Guid conversationId, Guid messageId, string token, string action, string label)
        : base(tenantId)
    {
        ConversationId = conversationId;
        MessageId = messageId;
        Token = token;
        Action = action;
        Label = label;
    }

    public Guid ConversationId { get; private set; }

    public Guid MessageId { get; private set; }

    /// <summary>128 random bits, base64url without padding (22 characters).</summary>
    public string Token { get; private set; }

    /// <summary>One of <c>Contracts.RichAction</c>.</summary>
    public string Action { get; private set; }

    /// <summary>What the customer saw, e.g. "🛒 Lo voglio"; shown in the tap bubble.</summary>
    public string Label { get; private set; }

    /// <summary>Position in the message: card index × 10 + button index, or the option index.</summary>
    public int Position { get; private set; }

    public Guid? ProductId { get; private set; }

    public Guid? VariantId { get; private set; }

    /// <summary>A chosen value that is not a variant, e.g. a delivery zone from <c>show_choices</c>.</summary>
    public string? Value { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>First tap. Later taps still show as messages but act on the current state.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public string Payload => PayloadPrefix + Token;

    public static MessageAction Create(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        int position,
        string action,
        string label,
        Guid? productId,
        Guid? variantId,
        string? value,
        DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(action) || action.Length > ActionMaxLength)
        {
            throw new ArgumentException("Unknown action.", nameof(action));
        }

        if (value is { Length: > ValueMaxLength })
        {
            throw new ArgumentException("Value is too long.", nameof(value));
        }

        return new MessageAction(tenantId, conversationId, messageId, NewToken(), action, Trim(label, LabelMaxLength))
        {
            Position = position,
            ProductId = productId,
            VariantId = variantId,
            Value = value,
            ExpiresAt = now + Lifetime,
        };
    }

    /// <summary>The token of a channel payload, or null when it is not one of ours.</summary>
    public static string? TokenOf(string? payload) =>
        payload is { Length: 4 + TokenLength } && payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)
        && payload.AsSpan(PayloadPrefix.Length).IndexOfAnyExcept(TokenChars) < 0
            ? payload[PayloadPrefix.Length..]
            : null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];
}
