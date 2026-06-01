namespace OpenBI.Interfaces;

/// <summary>
/// The MCP session active for the current scope, bound to a single site.
/// </summary>
public sealed record CurrentSession(string IdSession, string IdSite);
