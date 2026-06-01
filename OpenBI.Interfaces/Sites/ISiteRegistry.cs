namespace OpenBI.Interfaces.Sites;

/// <summary>
/// Cached site definitions for the MCP host.
/// </summary>
public interface ISiteRegistry
{
    IReadOnlyList<RegisteredSite> All { get; }

    bool TryGet(string idSite, out RegisteredSite? site);
}
