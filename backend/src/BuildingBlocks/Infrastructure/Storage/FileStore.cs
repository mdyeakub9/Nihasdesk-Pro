using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nihasdesk.Infrastructure.Storage;

/// <summary>
/// Private storage for uploaded files (SEC-04): random keys, never the client's file name, never
/// the web root. Files are read only by the backend. Object storage replaces the local folder later.
/// </summary>
public interface IFileStore
{
    /// <summary>Stores the content under a new random key inside the workspace's folder, and returns the key.</summary>
    Task<string> SaveAsync(Guid tenantId, Stream content, CancellationToken cancellationToken);

    /// <summary>The caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Does nothing when the file is already gone.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>A folder on disk, shared by the API and Workers (<c>Storage:LocalPath</c>).</summary>
public sealed partial class LocalFileStore(string root) : IFileStore
{
    public async Task<string> SaveAsync(Guid tenantId, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var key = $"{tenantId:N}/{Guid.NewGuid():N}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read));

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    // Keys come from this class; still checked, so a bad value can never leave the folder.
    private string PathOf(string key) =>
        KeyPattern().IsMatch(key ?? string.Empty)
            ? Path.Combine(root, key!.Replace('/', Path.DirectorySeparatorChar))
            : throw new ArgumentException("Invalid file key.", nameof(key));

    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}

public static class FileStoreServiceCollectionExtensions
{
    public static IServiceCollection AddFileStore(this IServiceCollection services, IConfiguration configuration)
    {
        var root = configuration["Storage:LocalPath"];
        services.TryAddSingleton<IFileStore>(new LocalFileStore(
            string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nihasdesk", "files")
                : root));
        return services;
    }
}
