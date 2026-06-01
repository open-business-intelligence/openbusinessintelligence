using OpenBI.Interfaces;

namespace OpenBI.MCP.Server.Application.Services;

internal sealed class CurrentSessionManager : ICurrentSessionManager
{
    private readonly SessionStore _sessionStore;

    public CurrentSessionManager(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    public CurrentSession? Current { get; private set; }

    public void SetIdSession(string idSession)
    {
        if (string.IsNullOrWhiteSpace(idSession))
            throw new ArgumentException("idSession is required.", nameof(idSession));

        var trimmed = idSession.Trim();
        if (!Guid.TryParse(trimmed, out var sessionGuid))
            throw new ArgumentException("idSession must be a valid GUID.", nameof(idSession));

        var idSite = _sessionStore.GetIdSite(sessionGuid);
        Current = new CurrentSession(trimmed, idSite);
    }
}
