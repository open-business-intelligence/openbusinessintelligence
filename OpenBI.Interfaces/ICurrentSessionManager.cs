namespace OpenBI.Interfaces;

/// <summary>
/// Holds the current MCP session for the active request scope.
/// Each MCP tool that accepts a session_id should call <see cref="SetIdSession"/> near the start of its method
/// so downstream services can read session and site without passing them as connection parameters.
/// </summary>
public interface ICurrentSessionManager
{
    /// <summary>
    /// Session and site for the current scope, or <c>null</c> if <see cref="SetIdSession"/> has not been called.
    /// </summary>
    CurrentSession? Current { get; }

    /// <summary>
    /// Sets the session for the current scope. Resolves <c>idSite</c> from the host <c>SessionStore</c>.
    /// </summary>
    /// <param name="idSession">Non-empty session id (typically the GUID returned by <c>create_session</c>).</param>
    void SetIdSession(string idSession);
}
