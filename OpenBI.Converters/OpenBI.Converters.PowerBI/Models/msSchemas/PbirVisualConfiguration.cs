using System.Collections.Generic;

namespace OpenBI.Converters.PowerBI.Models.msSchemas;

// Dedicated msSchemas visual payload model. This mirrors the PBIR visual payload
// shape used by the parser (similar to Layout.Config) while preserving unknown fields.
public sealed class PbirVisualConfiguration
{    
    [Newtonsoft.Json.JsonProperty("layouts")]
    public object? Layouts { get; set; }

    [Newtonsoft.Json.JsonProperty("singleVisual")]
    public object? SingleVisual { get; set; }

    [Newtonsoft.Json.JsonProperty("version")]
    public string? Version { get; set; }

    [Newtonsoft.Json.JsonProperty("themeCollection")]
    public object? ThemeCollection { get; set; }

    [Newtonsoft.Json.JsonProperty("activeSectionIndex")]
    public int? ActiveSectionIndex { get; set; }

    [Newtonsoft.Json.JsonProperty("defaultDrillFilterOtherVisuals")]
    public bool? DefaultDrillFilterOtherVisuals { get; set; }

    [Newtonsoft.Json.JsonProperty("settings")]
    public object? Settings { get; set; }

    [Newtonsoft.Json.JsonProperty("objects")]
    public object? Objects { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> AdditionalProperties { get; set; } = new Dictionary<string, object?>();
}
