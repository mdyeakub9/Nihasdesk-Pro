namespace Nihasdesk.Modules.Commerce.Contracts;

/// <summary>
/// Public links to product images (ADR 0031 §7), for channels that fetch the picture themselves, such
/// as Messenger cards. Signed (HMAC) and valid for 7 days; they open product images only, never
/// customer media. Storage keys never leave the Commerce module.
/// </summary>
public interface IProductImageLinks
{
    /// <summary>
    /// A link to the card-size copy of a saved image of the current workspace, or null when public
    /// links aren't configured (<c>App:PublicUrl</c> over https and <c>Commerce:Images:SigningKey</c>).
    /// </summary>
    Task<string?> CardUrlAsync(Guid imageId, CancellationToken cancellationToken);
}
