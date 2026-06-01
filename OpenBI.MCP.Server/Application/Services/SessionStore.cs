using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenBI.MCP.Server.Infrastructure.Persistence;

namespace OpenBI.MCP.Server.Application.Services;

public class SessionStore
{
    private readonly ConcurrentDictionary<Guid, string> _sessions = new();
    private readonly string _dataDirectory;
    private readonly ILogger<SessionStore> _logger;

    public SessionStore(ILogger<SessionStore> logger)
    {
        _logger = logger;
        _dataDirectory = Path.Combine(AppContext.BaseDirectory, "sessions");
        Directory.CreateDirectory(_dataDirectory);
    }

    /// <summary>
    /// Creates a session bound to the given site id (must exist in <see cref="OpenBI.Interfaces.Sites.ISiteRegistry"/>).
    /// </summary>
    public Guid CreateSession(string idSite)
    {
        var sessionId = Guid.NewGuid();
        _sessions[sessionId] = idSite;

        using var ctx = CreateContext(sessionId);
        ctx.Database.EnsureCreated();

        _logger.LogInformation("Session created: {SessionId} for site {IdSite}", sessionId, idSite);
        return sessionId;
    }

    public string GetIdSite(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var idSite))
            throw new InvalidOperationException($"Session {sessionId} does not exist.");
        return idSite;
    }

    public OpenBIDbContext GetContext(Guid sessionId)
    {
        if (!_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"Session {sessionId} does not exist.");

        return CreateContext(sessionId);
    }

    public void CloseSession(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out _))
            throw new InvalidOperationException($"Session {sessionId} does not exist.");

        _logger.LogInformation("Session closed: {SessionId}", sessionId);
    }

    public void DeleteSession(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);

        var dbPath = GetDbPath(sessionId);
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        // Remove the session subfolder that holds artifact cache and any other session files.
        var sessionDir = Path.Combine(_dataDirectory, sessionId.ToString());
        if (Directory.Exists(sessionDir))
            Directory.Delete(sessionDir, recursive: true);

        _logger.LogInformation("Session deleted: {SessionId}", sessionId);
    }

    private OpenBIDbContext CreateContext(Guid sessionId)
    {
        var dbPath = GetDbPath(sessionId);
        var options = new DbContextOptionsBuilder<OpenBIDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new OpenBIDbContext(options);
    }

    private string GetDbPath(Guid sessionId) =>
        Path.Combine(_dataDirectory, $"{sessionId}.db");
}
