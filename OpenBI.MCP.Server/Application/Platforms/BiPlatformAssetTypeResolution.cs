using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.Application.Platforms;

/// <summary>
/// Resolves a platform <c>asset_type_id</c> to the <see cref="Infrastructure.Persistence.Entities.DbAssetInfo.ExternalType"/> string.
/// </summary>
public static class BiPlatformAssetTypeResolution
{
    public static bool TryResolve(
        RegisteredSite site,
        BiPlatformRegistry registry,
        string assetTypeId,
        out string externalType,
        out string? errorMessage)
    {
        externalType = "";
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(assetTypeId))
        {
            errorMessage = "asset_type_id is required.";
            return false;
        }

        if (!BiPlatformRegistry.IsSafeAssetTypeIdParameter(assetTypeId))
        {
            errorMessage =
                "Invalid asset_type_id: use only safe file-name characters (no path separators, wildcards, or '..').";
            return false;
        }

        var trimmedId = assetTypeId.Trim();

        if (!registry.TryGetPlatform(site.IdPlatform, out var platformEntry) || platformEntry is null)
        {
            errorMessage =
                $"Site's idPlatform '{site.IdPlatform}' is not registered under platforms/. Add platforms/{site.IdPlatform}/ or fix sites/*.json.";
            return false;
        }

        if (platformEntry.AssetTypes.Count > 0)
        {
            var match = platformEntry.AssetTypes.FirstOrDefault(a =>
                string.Equals(a.Id, trimmedId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                errorMessage =
                    $"Unknown asset_type_id '{trimmedId}' for platform '{site.IdPlatform}'. Call list_bi_platform_asset_types with platform_id '{site.IdPlatform}'.";
                return false;
            }

            externalType = match.Id;
            return true;
        }

        externalType = trimmedId;
        return true;
    }
}
