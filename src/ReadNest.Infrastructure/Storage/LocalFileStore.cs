using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;

namespace ReadNest.Infrastructure.Storage;

/// <summary>Configuration section "Storage".</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Folder for uploaded files. Relative paths are resolved against the content root. It must be outside
    /// wwwroot and, in containers, on a persistent volume.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/storage";
}

/// <summary>
/// Stores files on the local file system under <see cref="StorageOptions.RootPath"/>. Keys have the form
/// <c>{container}/{year}/{random}.{extension}</c> and are validated before any file system access, so a key can
/// never point outside the storage folder.
/// </summary>
public sealed partial class LocalFileStore : IFileStore
{
    private const int BufferSize = 81_920;
    private readonly string _root;
    private readonly string _staging;
    private readonly TimeProvider _time;
    private readonly ILogger<LocalFileStore> _logger;

    public LocalFileStore(IOptions<StorageOptions> options, IHostEnvironment environment, TimeProvider time, ILogger<LocalFileStore> logger)
    {
        var configured = options.Value.RootPath;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured)));
        _staging = Path.Combine(_root, ".staging");
        _time = time;
        _logger = logger;
        Directory.CreateDirectory(_staging);
    }

    public string RootPath => _root;

    public static bool IsValidKey(string? key) => key is not null && KeyRegex().IsMatch(key);

    public async Task<IStagedFile> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_staging, Guid.NewGuid().ToString("N") + ".upload");
        var staged = new StagedFile(this, path);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new FileTooLargeException(maxBytes);
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                staged.Length = total;
            }

            staged.Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            return staged;
        }
        catch
        {
            await staged.DisposeAsync();
            throw;
        }
    }

    public async Task<string> SaveAsync(string container, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var key = NewKey(container, extension);
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, content.ToArray(), cancellationToken);
        File.Move(temp, path);
        return key;
    }

    public Stream? OpenRead(string key)
    {
        if (!IsValidKey(key))
        {
            return null;
        }

        try
        {
            return new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning("Stored file {Key} is missing.", key);
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
        {
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The database no longer references the file, so a leftover only costs disk space.
            _logger.LogWarning(ex, "Could not delete stored file {Key}.", key);
        }

        return Task.CompletedTask;
    }

    private string NewKey(string container, string extension)
    {
        var year = _time.GetUtcNow().Year.ToString("0000", CultureInfo.InvariantCulture);
        var key = $"{container}/{year}/{Guid.NewGuid():N}.{extension}";
        return IsValidKey(key) ? key : throw new ArgumentException("Invalid storage container or extension.");
    }

    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("Storage key resolves outside the storage folder.");
    }

    [GeneratedRegex("^(photos|books)/[0-9]{4}/[0-9a-f]{32}\\.(webp|pdf|epub)$")]
    private static partial Regex KeyRegex();

    private sealed class StagedFile(LocalFileStore store, string path) : IStagedFile
    {
        private bool _committed;

        public long Length { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public Stream OpenRead() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        public Task<string> CommitAsync(string container, string extension, CancellationToken cancellationToken)
        {
            var key = store.NewKey(container, extension);
            var target = store.PathFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(path, target);
            _committed = true;
            return Task.FromResult(key);
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Staging leftovers are harmless; they are never served.
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
