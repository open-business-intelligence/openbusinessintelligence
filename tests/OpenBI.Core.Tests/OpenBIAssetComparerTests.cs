using OpenBI.Patching;
using Xunit;

namespace OpenBI.Core.Tests;

public class OpenBIAssetComparerTests
{
    [Fact]
    public void Compare_identical_asset_info_produces_no_changes()
    {
        var asset = CreateAsset("asset-1", "Sales Report");

        var changes = OpenBIAssetComparer.Compare(asset, asset);

        Assert.Empty(changes);
    }

    [Fact]
    public void Compare_renamed_asset_info_emits_replace()
    {
        var from = CreateAsset("asset-1", "Sales Report");
        var to = CreateAsset("asset-1", "Sales Dashboard");

        var changes = OpenBIAssetComparer.Compare(from, to);

        var replace = Assert.Single(changes);
        Assert.Equal(OpenBIEntity.AssetInfo, replace.Entity);
        Assert.Equal(OpenBIChangeOp.Replace, replace.Op);
        Assert.Equal("asset-1", replace.Id);
        Assert.Contains(replace.Parts, p => p.Property == "name");
    }

    [Fact]
    public void Compare_added_refresh_task_emits_add()
    {
        var from = CreateAsset("asset-1", "Report");
        var to = CreateAsset("asset-1", "Report");
        to.RefreshTasks = new List<RefreshTask>
        {
            new() { Id = "rt-1" }
        };

        var changes = OpenBIAssetComparer.Compare(from, to);

        var add = Assert.Single(changes);
        Assert.Equal(OpenBIEntity.RefreshTask, add.Entity);
        Assert.Equal(OpenBIChangeOp.Add, add.Op);
        Assert.Null(add.Id);
        Assert.NotNull(add.ValueJson);
        Assert.Contains("rt-1", add.ValueJson);
    }

    private static Asset CreateAsset(string id, string name)
    {
        return new Asset
        {
            Dependencies = Array.Empty<Asset>(),
            Info = new AssetInfo
            {
                Id = id,
                Name = name,
                Description = string.Empty,
                ExternalType = "Report",
                IdFolder = "folder-1",
                FolderName = "Reports",
                Type = AssetType.Report
            }
        };
    }
}
