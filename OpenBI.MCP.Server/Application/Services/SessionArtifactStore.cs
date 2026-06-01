using Microsoft.Extensions.Logging;

namespace OpenBI.MCP.Server.Application.Services;

/// <summary>
/// Persists raw artifact bytes (e.g. PBIX) to disk for the duration of a session.
/// Used by <c>download_asset</c> to cache the original artifact and by
/// <c>upload_asset</c> to detect existing assets and drive the patch flow.
/// <para>
/// Layout: <c>{BaseDirectory}/sessions/{sessionId}/artifacts/{assetId}.bin</c>
/// </para>
/// </summary>
public sealed class SessionArtifactStore
{
    private readonly string _sessionsDirectory;
    private readonly ILogger<SessionArtifactStore> _logger;

    public SessionArtifactStore(ILogger<SessionArtifactStore> logger)
    {
        _logger = logger;
        _sessionsDirectory = Path.Combine(AppContext.BaseDirectory, "sessions");
    }

    /// <summary>Persists artifact bytes for the given session + asset id pair.</summary>
    public async Task SaveArtifactAsync(Guid sessionId, string assetId, byte[] bytes, CancellationToken ct = default)
    {
        var dir = GetArtifactDir(sessionId);
        Directory.CreateDirectory(dir);

        var path = GetArtifactPath(sessionId, assetId);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

        _logger.LogDebug("SessionArtifactStore: saved {Bytes} bytes for session {SessionId} asset {AssetId}",
            bytes.Length, sessionId, assetId);
    }

    /// <summary>
    /// Tries to load a previously saved artifact. Returns <c>null</c> if no artifact
    /// was stored for this session + asset id (i.e. the asset is new, not downloaded).
    /// </summary>
    public async Task<byte[]?> TryLoadArtifactAsync(Guid sessionId, string assetId, CancellationToken ct = default)
    {
        var path = GetArtifactPath(sessionId, assetId);
        if (!File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);

        _logger.LogDebug("SessionArtifactStore: loaded {Bytes} bytes for session {SessionId} asset {AssetId}",
            bytes.Length, sessionId, assetId);

        return bytes;
    }

    /// <summary>Deletes all artifacts stored for a session. Called when the session is deleted.</summary>
    public void DeleteSessionArtifacts(Guid sessionId)
    {
        var dir = GetArtifactDir(sessionId);
        if (!Directory.Exists(dir)) return;

        Directory.Delete(dir, recursive: true);
        _logger.LogDebug("SessionArtifactStore: deleted artifacts for session {SessionId}", sessionId);
    }

    private string GetArtifactDir(Guid sessionId) =>
        Path.Combine(_sessionsDirectory, sessionId.ToString(), "artifacts");

    private string GetArtifactPath(Guid sessionId, string assetId) =>
        Path.Combine(GetArtifactDir(sessionId), $"{assetId}.bin");
}
