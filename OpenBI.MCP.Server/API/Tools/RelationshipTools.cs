using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class RelationshipTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_relationships", ReadOnly = true)]
    [Description("Returns all relationships in the session's OpenBI asset.")]
    public async Task<string> GetRelationships(
        [Description("The session ID.")] string session_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<RelationshipTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbRels = await ctx.Relationships.AsNoTracking().ToListAsync(ct);
        var relationships = dbRels.Select(r => new
        {
            rowId = r.RowId,
            relationship = OpenBIMapper.MapDbRelationship(r)
        }).ToList();
        return JsonSerializer.Serialize(new { relationships }, JsonOpts);
    }

    [McpServerTool(Name = "get_relationship", ReadOnly = true)]
    [Description("Returns a single relationship by its row ID.")]
    public async Task<string> GetRelationship(
        [Description("The session ID.")] string session_id,
        [Description("The relationship row ID (integer).")] int relationship_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<RelationshipTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbRel = await ctx.Relationships.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RowId == relationship_id, ct);
        if (dbRel is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Relationship {relationship_id} not found." });

        return JsonSerializer.Serialize(OpenBIMapper.MapDbRelationship(dbRel), JsonOpts);
    }

    [McpServerTool(Name = "create_relationship")]
    [Description("Creates a new relationship between two columns. Returns the generated row ID.")]
    public async Task<string> CreateRelationship(
        [Description("The session ID.")] string session_id,
        [Description("Source column ID.")] string id_column_from,
        [Description("Target column ID.")] string id_column_to,
        [Description("Relationship type: OneToMany, ManyToOne, ManyToMany, OneToOne.")] string relationship_type,
        [Description("Optional relationship name.")] string? name,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<RelationshipTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var assetInfo = await ctx.AssetInfos.FirstOrDefaultAsync(ct);
        if (assetInfo is null)
            return JsonSerializer.Serialize(new { status = "Error", message = "No asset loaded in session." });

        if (!Enum.TryParse<RelationshipDirection>(relationship_type, true, out var relType))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid relationship type: '{relationship_type}'. Valid values: OneToMany, ManyToOne, ManyToMany, OneToOne." });

        var dbRel = new DbRelationship
        {
            Name = name,
            IdColumnFrom = id_column_from,
            IdColumnTo = id_column_to,
            Type = (int)relType,
            AssetInfoId = assetInfo.Id
        };

        ctx.Relationships.Add(dbRel);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_relationship: {RowId} from {From} to {To}", dbRel.RowId, id_column_from, id_column_to);
        return JsonSerializer.Serialize(new { status = "Success", relationshipId = dbRel.RowId });
    }

    [McpServerTool(Name = "delete_relationship")]
    [Description("Deletes a relationship by its row ID.")]
    public async Task<string> DeleteRelationship(
        [Description("The session ID.")] string session_id,
        [Description("The relationship row ID to delete.")] int relationship_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<RelationshipTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbRel = await ctx.Relationships.FirstOrDefaultAsync(r => r.RowId == relationship_id, ct);
        if (dbRel is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Relationship {relationship_id} not found." });

        ctx.Relationships.Remove(dbRel);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_relationship: {RowId}", relationship_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }
}
