using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Domain;

internal enum AttachmentKind
{
    Image,
    Audio,
    Video,
    File,
    Sticker,
}

internal enum AttachmentStatus
{
    /// <summary>Waiting for the download job.</summary>
    Pending,
    Saved,
    Failed,
    TooLarge,
}

/// <summary>Why media can't be shown; codes the dashboard turns into plain words.</summary>
internal static class AttachmentFailure
{
    /// <summary>The channel's link no longer works: the team asks the customer to send it again.</summary>
    public const string Expired = "expired";

    /// <summary>Not a type we keep, or not a real file of the type it claims to be (SEC-04).</summary>
    public const string TypeNotAllowed = "type_not_allowed";

    public const string TooLarge = "too_large";

    /// <summary>Kept failing after the retries.</summary>
    public const string Network = "network";

    /// <summary>The link pointed somewhere we never fetch from (SEC-05).</summary>
    public const string SourceNotAllowed = "source_not_allowed";
}

/// <summary>Stored as lower-case text with an underscore, as the API shows it.</summary>
internal static class AttachmentText
{
    public static string Of(AttachmentStatus status) =>
        status == AttachmentStatus.TooLarge ? "too_large" : status.ToString().ToLowerInvariant();

    public static AttachmentStatus Status(string value) =>
        value == "too_large" ? AttachmentStatus.TooLarge : Enum.Parse<AttachmentStatus>(value, ignoreCase: true);

    public static string Of(AttachmentKind kind) => kind.ToString().ToLowerInvariant();

    public static AttachmentKind Kind(string value) => Enum.Parse<AttachmentKind>(value, ignoreCase: true);
}

/// <summary>
/// A photo, voice note, video or file of a message (ADR 0031 §1). The channel's link is kept only until
/// the download job is done; after that the file is ours, under keys that never come from a file name.
/// </summary>
internal sealed class MessageAttachment : TenantEntity
{
    public const int SourceUrlMaxLength = 4096;
    public const int FailureCodeMaxLength = 30;
    public const int ContentTypeMaxLength = 100;
    public const int Sha256Length = 64;
    public const int StorageKeyMaxLength = 200;
    public const int FileNameMaxLength = 120;

    private MessageAttachment(Guid tenantId, Guid messageId, int position, AttachmentKind kind, string? sourceUrl)
        : base(tenantId)
    {
        MessageId = messageId;
        Position = position;
        Kind = kind;
        SourceUrl = sourceUrl;
    }

    public Guid MessageId { get; private set; }

    /// <summary>Order within the message; with the message it is unique, so a retry can't add a second copy.</summary>
    public int Position { get; private set; }

    /// <summary>What the content turned out to be, once saved.</summary>
    public AttachmentKind Kind { get; private set; }

    public AttachmentStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SourceUrl { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Detected from the content, never from the channel's word.</summary>
    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    /// <summary>Of the file as it arrived.</summary>
    public string? Sha256 { get; private set; }

    /// <summary>For an image this is the re-encoded copy: the file as sent is never kept.</summary>
    public string? StorageKey { get; private set; }

    public string? ThumbnailKey { get; private set; }

    public string? AiImageKey { get; private set; }

    /// <summary>Only to show and to name a download; cleaned, and never part of a storage path.</summary>
    public string? FileName { get; private set; }

    public static MessageAttachment Incoming(Guid tenantId, Guid messageId, int position, AttachmentKind kind, string? sourceUrl, DateTimeOffset now) =>
        new(tenantId, messageId, position, kind, sourceUrl) { NextAttemptAt = now };

    public void SavedImage(string contentType, long size, int width, int height, string sha256, string key, string thumbnailKey, string aiImageKey)
    {
        // A sticker stays a sticker; anything else that decodes as an image is an image.
        Kind = Kind == AttachmentKind.Sticker ? AttachmentKind.Sticker : AttachmentKind.Image;
        ContentType = contentType;
        SizeBytes = size;
        Width = width;
        Height = height;
        Sha256 = sha256;
        StorageKey = key;
        ThumbnailKey = thumbnailKey;
        AiImageKey = aiImageKey;
        Finish(AttachmentStatus.Saved, null);
    }

    public void SavedFile(AttachmentKind kind, string contentType, long size, string sha256, string key, string? fileName)
    {
        Kind = kind;
        ContentType = contentType;
        SizeBytes = size;
        Sha256 = sha256;
        StorageKey = key;
        FileName = fileName;
        Finish(AttachmentStatus.Saved, null);
    }

    public void Failed(string code) => Finish(AttachmentStatus.Failed, code);

    public void WasTooLarge() => Finish(AttachmentStatus.TooLarge, AttachmentFailure.TooLarge);

    public void RetryAt(DateTimeOffset at) => NextAttemptAt = at;

    private void Finish(AttachmentStatus status, string? failureCode)
    {
        Status = status;
        FailureCode = failureCode;
        SourceUrl = null;
        NextAttemptAt = null;
    }
}
