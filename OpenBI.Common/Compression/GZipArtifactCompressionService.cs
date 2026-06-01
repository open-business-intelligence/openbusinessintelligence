using System.IO.Compression;
using OpenBI.Interfaces.Infrastructure;

namespace OpenBI.Common.Compression;

/// <summary>
/// Default GZip-based implementation of <see cref="IArtifactCompressionService"/>.
/// Depends only on the BCL — no external packages required.
/// </summary>
public class GZipArtifactCompressionService : IArtifactCompressionService
{
    private const CompressionLevel DefaultLevel = CompressionLevel.Optimal;
    private const int DefaultBufferSize = 81_920; // 80 KB

    public async Task ZipAsync(Stream source, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        await using var gzip = new GZipStream(destination, DefaultLevel, leaveOpen: true);
        await source.CopyToAsync(gzip, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnzipAsync(Stream source, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        await gzip.CopyToAsync(destination, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ZipAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();
        await ZipAsync(source, destination, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    public async Task<byte[]> UnzipAsync(byte[] compressedData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compressedData);
        using var source = new MemoryStream(compressedData);
        using var destination = new MemoryStream();
        await UnzipAsync(source, destination, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    public async Task ZipFolderAsync(string sourceFolderPath, Stream destination, CancellationToken cancellationToken = default, IReadOnlySet<string>? excludedDirectoryNames = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFolderPath);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException($"Source folder not found: {sourceFolderPath}.");

        var fullBasePath = Path.GetFullPath(sourceFolderPath);
        if (!fullBasePath.EndsWith(Path.DirectorySeparatorChar))
            fullBasePath += Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var files = excludedDirectoryNames is { Count: > 0 }
            ? EnumerateFilesExcluding(fullBasePath, excludedDirectoryNames)
            : Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);

        foreach (var fullPath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(fullBasePath, fullPath).Replace('\\', '/');
            var entry = archive.CreateEntry(relativePath, DefaultLevel);
            await using var fileStream = File.OpenRead(fullPath);
            await using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<byte[]> ZipFolderAsync(string sourceFolderPath, CancellationToken cancellationToken = default, IReadOnlySet<string>? excludedDirectoryNames = null)
    {
        using var ms = new MemoryStream();
        await ZipFolderAsync(sourceFolderPath, ms, cancellationToken, excludedDirectoryNames).ConfigureAwait(false);
        return ms.ToArray();
    }

    public async Task<byte[]> ZipEntriesAsync(IEnumerable<(string entryPath, byte[] content)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryPath, content) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entryPath)) continue;

                var normalized = entryPath.Replace('\\', '/').TrimStart('/');
                if (normalized.Split('/').Any(s => s == ".."))
                    throw new InvalidOperationException($"Entry path escapes archive: {entryPath}.");

                var entry = archive.CreateEntry(normalized, DefaultLevel);
                var bytes = content ?? Array.Empty<byte>();
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        return ms.ToArray();
    }

    public async Task UnzipToFolderAsync(Stream source, string destinationFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationFolderPath);
        Directory.CreateDirectory(destinationFolderPath);
        var fullBase = Path.GetFullPath(destinationFolderPath);
        if (!fullBase.EndsWith(Path.DirectorySeparatorChar))
            fullBase += Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(Path.Combine(fullBase, entry.FullName));
            if (!fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Entry escapes destination: {entry.FullName}.");

            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(fullPath.TrimEnd(Path.DirectorySeparatorChar, '/')); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(fullPath);
            await entryStream.CopyToAsync(fileStream, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UnzipToFolderAsync(byte[] zipBytes, string destinationFolderPath, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(zipBytes);
        await UnzipToFolderAsync(ms, destinationFolderPath, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateFilesExcluding(string dir, IReadOnlySet<string> excluded)
    {
        foreach (var f in Directory.EnumerateFiles(dir)) yield return f;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (excluded.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var f in EnumerateFilesExcluding(sub, excluded)) yield return f;
        }
    }
}
