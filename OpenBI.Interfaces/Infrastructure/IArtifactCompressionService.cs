namespace OpenBI.Interfaces.Infrastructure;

/// <summary>
/// Service for compressing and decompressing data using a configurable algorithm (GZip by default).
/// </summary>
public interface IArtifactCompressionService
{
    /// <summary>
    /// Compresses data from the source stream and writes it to the destination stream.
    /// </summary>
    Task ZipAsync(Stream source, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses data from the source stream and writes it to the destination stream.
    /// </summary>
    Task UnzipAsync(Stream source, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses the given bytes and returns the compressed result.
    /// </summary>
    Task<byte[]> ZipAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses the given bytes and returns the raw result.
    /// </summary>
    Task<byte[]> UnzipAsync(byte[] compressedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a ZIP archive of the given folder and writes it to the destination stream.
    /// Only files are included; directories are implied by entry paths.
    /// </summary>
    Task ZipFolderAsync(string sourceFolderPath, Stream destination, CancellationToken cancellationToken = default, IReadOnlySet<string>? excludedDirectoryNames = null);

    /// <summary>
    /// Creates a ZIP archive of the given folder and returns it as a byte array.
    /// </summary>
    Task<byte[]> ZipFolderAsync(string sourceFolderPath, CancellationToken cancellationToken = default, IReadOnlySet<string>? excludedDirectoryNames = null);

    /// <summary>
    /// Creates a ZIP archive from in-memory entries (entry path and content).
    /// Entry paths are normalized to use forward slashes; paths that escape the archive (containing "..") are rejected.
    /// </summary>
    Task<byte[]> ZipEntriesAsync(IEnumerable<(string entryPath, byte[] content)> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a ZIP archive from the source stream to the given folder.
    /// </summary>
    Task UnzipToFolderAsync(Stream source, string destinationFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a ZIP archive from the given bytes to the given folder.
    /// </summary>
    Task UnzipToFolderAsync(byte[] zipBytes, string destinationFolderPath, CancellationToken cancellationToken = default);
}
