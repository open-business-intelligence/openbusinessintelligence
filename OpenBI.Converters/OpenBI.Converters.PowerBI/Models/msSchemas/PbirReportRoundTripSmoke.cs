using System;
using System.IO;
using System.Linq;

namespace OpenBI.Converters.PowerBI.Models.msSchemas;

public static class PbirReportRoundTripSmoke
{
    public static PbirRoundTripSmokeResult Validate(string inputDirectory)
    {
        var first = PbirReportSerializer.Deserialize(inputDirectory);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "pbir_roundtrip_" + Guid.NewGuid().ToString("N"));
        PbirReportSerializer.Serialize(first, outputDirectory);
        var second = PbirReportSerializer.Deserialize(outputDirectory);

        try
        {
            var firstPageCount = first.Pages.Count;
            var secondPageCount = second.Pages.Count;
            var firstVisualCount = first.Pages.Sum(p => p.Visuals.Count);
            var secondVisualCount = second.Pages.Sum(p => p.Visuals.Count);
            var firstBookmarkCount = first.Bookmarks.Count;
            var secondBookmarkCount = second.Bookmarks.Count;

            var pageIdsMatch = first.Pages.Select(p => p.PageId).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(second.Pages.Select(p => p.PageId).OrderBy(x => x, StringComparer.Ordinal));
            var visualIdsMatch = first.Pages.SelectMany(p => p.Visuals.Select(v => $"{p.PageId}/{v.VisualId}"))
                .OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(second.Pages.SelectMany(p => p.Visuals.Select(v => $"{p.PageId}/{v.VisualId}"))
                    .OrderBy(x => x, StringComparer.Ordinal));
            var bookmarkIdsMatch = first.Bookmarks.Select(b => b.BookmarkId).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(second.Bookmarks.Select(b => b.BookmarkId).OrderBy(x => x, StringComparer.Ordinal));

            return new PbirRoundTripSmokeResult
            {
                InputDirectory = inputDirectory,
                OutputDirectory = outputDirectory,
                PageCount = firstPageCount,
                VisualCount = firstVisualCount,
                BookmarkCount = firstBookmarkCount,
                IsSuccess = firstPageCount == secondPageCount
                    && firstVisualCount == secondVisualCount
                    && firstBookmarkCount == secondBookmarkCount
                    && pageIdsMatch
                    && visualIdsMatch
                    && bookmarkIdsMatch
            };
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }
}

public sealed class PbirRoundTripSmokeResult
{
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int VisualCount { get; set; }
    public int BookmarkCount { get; set; }
    public bool IsSuccess { get; set; }
}
