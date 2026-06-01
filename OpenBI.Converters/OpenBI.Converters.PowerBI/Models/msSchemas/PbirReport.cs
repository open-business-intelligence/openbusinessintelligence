using System;
using System.Collections.Generic;
using System.Text;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Bookmark;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.BookmarksMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Page;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.PagesMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Report;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.ReportExtension;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualContainer;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VersionMetadata;

namespace OpenBI.Converters.PowerBI.Models.msSchemas;

public sealed class PbirReport
{
    public DefinitionPbirFile DefinitionPbir { get; set; } = new();
    public PlatformFile Platform { get; set; } = new();
    public VersionMetadataRoot VersionMetadata { get; set; } = new();
    public ReportRoot Report { get; set; } = new();
    public ReportExtensionRoot? ReportExtension { get; set; }
    public PagesMetadataRoot PagesMetadata { get; set; } = new();
    public List<PbirPage> Pages { get; set; } = [];
    public BookmarksMetadataRoot? BookmarksMetadata { get; set; }
    public List<PbirBookmark> Bookmarks { get; set; } = [];
}

public sealed class PbirPage
{
    public string PageId { get; set; } = string.Empty;
    public PageRoot Page { get; set; } = new();
    public List<PbirVisual> Visuals { get; set; } = [];
}

public sealed class PbirVisual
{
    public string VisualId { get; set; } = string.Empty;
    public VisualContainerRoot Visual { get; set; } = new();
}

public sealed class PbirBookmark
{
    public string BookmarkId { get; set; } = string.Empty;
    public BookmarkRoot Bookmark { get; set; } = new();
}

public sealed class DefinitionPbirFile
{
    [Newtonsoft.Json.JsonProperty("$schema")]
    public string? Schema { get; set; }

    [Newtonsoft.Json.JsonProperty("version")]
    public string? Version { get; set; }

    [Newtonsoft.Json.JsonProperty("datasetReference")]
    public DatasetReference? DatasetReference { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class DatasetReference
{
    [Newtonsoft.Json.JsonProperty("byPath")]
    public DatasetReferenceByPath? ByPath { get; set; }

    [Newtonsoft.Json.JsonProperty("byConnection")]
    public DatasetReferenceByConnection? ByConnection { get; set; }


    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class DatasetReferenceByPath
{
    [Newtonsoft.Json.JsonProperty("path")]
    public string? Path { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class DatasetReferenceByConnection
{
    private const string PowerBiMyOrgPrefix = "powerbi://api.powerbi.com/v1.0/myorg/";

    [Newtonsoft.Json.JsonProperty("connectionString")]
    public string? ConnectionString { get; set; }

    public DatasetReferenceByConnection WithConnectionString(string workspaceId, string workspaceName, string semanticModelName, string semanticModelId)
    {
        ConnectionString = $"Data Source=\"powerbi://api.powerbi.com/v1.0/myorg/{workspaceName}\";initial catalog=\"{semanticModelName}\";access mode=readonly;integrated security=ClaimsToken;semanticmodelid={semanticModelId}";
        return this;
    }

    /// <summary>
    /// Parses <see cref="ConnectionString"/> values produced by <see cref="WithConnectionString"/> (Power BI service dataset binding).
    /// </summary>
    public static bool TryParseSemanticModelBinding(string? connectionString, out string workspaceName, out string semanticModelName, out string semanticModelId)
    {
        workspaceName = string.Empty;
        semanticModelName = string.Empty;
        semanticModelId = string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var pairs = ParseConnectionStringKeyValues(connectionString);
        if (!pairs.TryGetValue("Data Source", out var dataSource) || string.IsNullOrWhiteSpace(dataSource))
            return false;
        if (!pairs.TryGetValue("initial catalog", out var catalog) || string.IsNullOrWhiteSpace(catalog))
            return false;
        if (!pairs.TryGetValue("semanticmodelid", out var modelId) || string.IsNullOrWhiteSpace(modelId))
            return false;

        workspaceName = ExtractWorkspaceNameFromDataSource(dataSource.Trim());
        semanticModelName = catalog.Trim();
        semanticModelId = modelId.Trim();
        return !string.IsNullOrWhiteSpace(workspaceName) && !string.IsNullOrWhiteSpace(semanticModelName) && !string.IsNullOrWhiteSpace(semanticModelId);
    }

    /// <summary>
    /// If <paramref name="dataSource"/> uses the standard Power BI org URL, returns the segment after <c>myorg/</c> (URL-decoded); otherwise the segment after the last slash, or the whole value.
    /// </summary>
    private static string ExtractWorkspaceNameFromDataSource(string dataSource)
    {
        var trimmed = dataSource.Trim().Trim('"');
        if (trimmed.StartsWith(PowerBiMyOrgPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed.AsSpan(PowerBiMyOrgPrefix.Length).TrimEnd('/').ToString();
            try
            {
                return Uri.UnescapeDataString(rest);
            }
            catch (UriFormatException)
            {
                return rest;
            }
        }

        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    /// <summary>
    /// Semicolon-separated key=value pairs; values may be double-quoted with <c>""</c> as escaped quote.
    /// </summary>
    private static Dictionary<string, string> ParseConnectionStringKeyValues(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < connectionString.Length)
        {
            while (i < connectionString.Length && (char.IsWhiteSpace(connectionString[i]) || connectionString[i] == ';'))
                i++;
            if (i >= connectionString.Length)
                break;

            var eq = connectionString.IndexOf('=', i);
            if (eq < 0)
                break;

            var key = connectionString.Substring(i, eq - i).Trim();
            i = eq + 1;
            if (i >= connectionString.Length)
            {
                if (key.Length > 0)
                    result[key] = string.Empty;
                break;
            }

            string value;
            if (connectionString[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < connectionString.Length)
                {
                    if (connectionString[i] == '"')
                    {
                        if (i + 1 < connectionString.Length && connectionString[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(connectionString[i++]);
                }

                value = sb.ToString();
            }
            else
            {
                var semi = connectionString.IndexOf(';', i);
                if (semi < 0)
                {
                    value = connectionString[i..].Trim();
                    i = connectionString.Length;
                }
                else
                {
                    value = connectionString.Substring(i, semi - i).Trim();
                    i = semi;
                }
            }

            if (key.Length > 0)
                result[key] = value;
        }

        return result;
    }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class PlatformFile
{
    [Newtonsoft.Json.JsonProperty("$schema")]
    public string? Schema { get; set; }

    [Newtonsoft.Json.JsonProperty("metadata")]
    public PlatformMetadata? Metadata { get; set; }

    [Newtonsoft.Json.JsonProperty("config")]
    public PlatformConfig? Config { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class PlatformMetadata
{
    [Newtonsoft.Json.JsonProperty("type")]
    public string? Type { get; set; }

    [Newtonsoft.Json.JsonProperty("displayName")]
    public string? DisplayName { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}

public sealed class PlatformConfig
{
    [Newtonsoft.Json.JsonProperty("version")]
    public string? Version { get; set; }

    [Newtonsoft.Json.JsonProperty("logicalId")]
    public string? LogicalId { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}
