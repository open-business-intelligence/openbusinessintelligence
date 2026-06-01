#nullable enable
using System.Collections.Generic;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualConfiguration;

namespace OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualContainer
{
    public sealed class VisualContainerRoot
    {
        [Newtonsoft.Json.JsonProperty("$schema")]
        public string? Schema { get; set; }

        [Newtonsoft.Json.JsonProperty("name")]
        public string? Name { get; set; }

        [Newtonsoft.Json.JsonProperty("position")]
        public VisualContainerPosition? Position { get; set; }

        [Newtonsoft.Json.JsonProperty("visual")]
        public VisualConfigurationRoot? Visual { get; set; }

        [Newtonsoft.Json.JsonProperty("visualGroup")]
        public VisualGroupConfig? VisualGroup { get; set; }

        [Newtonsoft.Json.JsonProperty("parentGroupName")]
        public string? ParentGroupName { get; set; }

        [Newtonsoft.Json.JsonProperty("filterConfig")]
        public object? FilterConfig { get; set; }

        [Newtonsoft.Json.JsonProperty("isHidden")]
        public bool? IsHidden { get; set; }

        [Newtonsoft.Json.JsonProperty("annotations")]
        public List<Annotation>? Annotations { get; set; }

        [Newtonsoft.Json.JsonProperty("howCreated")]
        public string? HowCreated { get; set; }
    }

    public sealed class VisualContainerPosition
    {
        [Newtonsoft.Json.JsonProperty("x")]
        public double? X { get; set; }

        [Newtonsoft.Json.JsonProperty("y")]
        public double? Y { get; set; }

        [Newtonsoft.Json.JsonProperty("z")]
        public double? Z { get; set; }

        [Newtonsoft.Json.JsonProperty("height")]
        public double? Height { get; set; }

        [Newtonsoft.Json.JsonProperty("width")]
        public double? Width { get; set; }

        [Newtonsoft.Json.JsonProperty("tabOrder")]
        public double? TabOrder { get; set; }

        [Newtonsoft.Json.JsonProperty("angle")]
        public double? Angle { get; set; }
    }

    public sealed class VisualGroupConfig
    {
        [Newtonsoft.Json.JsonProperty("displayName")]
        public string? DisplayName { get; set; }

        [Newtonsoft.Json.JsonProperty("groupMode")]
        public string? GroupMode { get; set; }

        [Newtonsoft.Json.JsonProperty("objects")]
        public VisualGroupFormattingObjects? Objects { get; set; }
    }

    public sealed class VisualGroupFormattingObjects
    {
        [Newtonsoft.Json.JsonProperty("background")]
        public List<FormattingRule>? Background { get; set; }

        [Newtonsoft.Json.JsonProperty("lockAspect")]
        public List<FormattingRule>? LockAspect { get; set; }

        [Newtonsoft.Json.JsonProperty("general")]
        public List<FormattingGeneralRule>? General { get; set; }
    }

    public sealed class FormattingRule
    {
        [Newtonsoft.Json.JsonProperty("selector")]
        public object? Selector { get; set; }

        [Newtonsoft.Json.JsonProperty("properties")]
        public object? Properties { get; set; }
    }

    public sealed class FormattingGeneralRule
    {
        [Newtonsoft.Json.JsonProperty("selector")]
        public object? Selector { get; set; }

        [Newtonsoft.Json.JsonProperty("properties")]
        public VisualGroupGeneralFormattingObjects? Properties { get; set; }
    }

    public sealed class VisualGroupGeneralFormattingObjects
    {
        [Newtonsoft.Json.JsonProperty("x")]
        public object? X { get; set; }

        [Newtonsoft.Json.JsonProperty("y")]
        public object? Y { get; set; }

        [Newtonsoft.Json.JsonProperty("width")]
        public object? Width { get; set; }

        [Newtonsoft.Json.JsonProperty("height")]
        public object? Height { get; set; }

        [Newtonsoft.Json.JsonProperty("altText")]
        public object? AltText { get; set; }
    }

    public sealed class Annotation
    {
        [Newtonsoft.Json.JsonProperty("name")]
        public string? Name { get; set; }

        [Newtonsoft.Json.JsonProperty("value")]
        public string? Value { get; set; }
    }
}