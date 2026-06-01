using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace OpenBI.Connectors.PowerBI.Models.Fabric
{
    public class WorkspaceResponse
    {
        public List<Workspace> Value { get; set; }
        public string? ContinuationUri { get; set; }
        public string? ContinuationToken { get; set; }
    }

    public class Workspace
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
        [JsonProperty("description")]
        public string Description { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("capacityId")]
        public Guid CapacityId { get; set; }
    }

    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);        

    public class Definition
    {
        public string? format { get; set; }

        public List<Part> parts { get; set; }
    }

    public class Part
    {
        public string path { get; set; }
        public string payload { get; set; }
        public string payloadType { get; set; }
    }

    public class GetItemDefinitionResponse
    {
        public Definition definition { get; set; }
    }

    public class LongRunningOperationStatus
    {
        public string status { get; set; }
        public DateTime createdTimeUtc { get; set; }
        public DateTime lastUpdatedTimeUtc { get; set; }
        public int? percentComplete { get; set; }
        /// <summary>Present when status is "Failed". Contains API error details.</summary>
        public FabricOperationError error { get; set; }
    }

    public class FabricOperationError
    {
        public string errorCode { get; set; }
        public string message { get; set; }
    }

    /// <summary>
    /// Parsed from .platform file in Report/SemanticModel zips. Used to get displayName for create.
    /// </summary>
    public class PlatformMetadata
    {
        public PlatformMetadataInner? metadata { get; set; }
    }

    public class PlatformMetadataInner
    {
        public string? type { get; set; }
        public string? displayName { get; set; }
    }

    /// <summary>
    /// Request body for Fabric Create Report / Create Semantic Model.
    /// </summary>
    public class CreateFabricItemRequest
    {
        public string displayName { get; set; } = "";
        public string? description { get; set; }
        public Definition? definition { get; set; }
    }

    /// <summary>
    /// Request body for Fabric Update Report Definition / Update Semantic Model Definition.
    /// </summary>
    public class UpdateDefinitionRequest
    {
        public Definition definition { get; set; } = new Definition();
    }

    /// <summary>
    /// Response from Fabric Create Report / Create Semantic Model (201 body).
    /// </summary>
    public class FabricItemResponse
    {
        public string? displayName { get; set; }
        public string? description { get; set; }
        public string? type { get; set; }
        public string? workspaceId { get; set; }
        public string? id { get; set; }
    }
}
