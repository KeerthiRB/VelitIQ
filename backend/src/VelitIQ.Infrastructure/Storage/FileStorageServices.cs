using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.Interfaces;

namespace VelitIQ.Infrastructure.Storage;

public sealed class LocalDiskFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public string ProviderName => "LocalDisk (Windows Server / UNC Ready)";

    public LocalDiskFileStorageService(IOptions<StorageOptions> options)
    {
        var configured = options.Value.LocalRootPath;
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Stream content, string relativeKey, string contentType, CancellationToken ct = default)
    {
        var safeRelative = relativeKey.Replace("..", "_").TrimStart('/', '\\');
        var fullPath = Path.Combine(_rootPath, safeRelative);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        content.Position = 0;
        await content.CopyToAsync(fs, ct);
        return safeRelative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Stored artifact not found at '{storagePath}'.");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}

public sealed class AzureBlobFileStorageService : IFileStorageService
{
    private readonly BlobContainerClient _container;

    public string ProviderName => "AzureBlobStorage";

    public AzureBlobFileStorageService(IOptions<StorageOptions> options)
    {
        var opts = options.Value;
        var serviceClient = new BlobServiceClient(opts.AzureBlobConnectionString);
        _container = serviceClient.GetBlobContainerClient(opts.AzureBlobContainerName);
    }

    public async Task<string> SaveAsync(Stream content, string relativeKey, string contentType, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blobKey = relativeKey.Replace('\\', '/').TrimStart('/');
        var blob = _container.GetBlobClient(blobKey);
        content.Position = 0;
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);
        return blobKey;
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(storagePath);
        return await blob.OpenReadAsync(cancellationToken: ct);
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(storagePath);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
