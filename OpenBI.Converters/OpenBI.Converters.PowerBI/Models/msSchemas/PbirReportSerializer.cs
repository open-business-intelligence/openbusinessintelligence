using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Bookmark;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.BookmarksMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Page;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.PagesMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Report;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.ReportExtension;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VersionMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualContainer;
using Newtonsoft.Json;

namespace OpenBI.Converters.PowerBI.Models.msSchemas;

public static class PbirReportSerializer
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        
    };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static PbirReport Deserialize(string inputDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputDirectory))
            throw new ArgumentException("Input directory is required.", nameof(inputDirectory));
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDirectory}");

        var definitionDirectory = Path.Combine(inputDirectory, "definition");
        var pagesDirectory = Path.Combine(definitionDirectory, "pages");
        var bookmarksDirectory = Path.Combine(definitionDirectory, "bookmarks");
        var reportExtensionsPath = Path.Combine(definitionDirectory, "reportExtensions.json");

        EnsureFileExists(Path.Combine(inputDirectory, "definition.pbir"));
        EnsureFileExists(Path.Combine(inputDirectory, ".platform"));
        EnsureFileExists(Path.Combine(definitionDirectory, "version.json"));
        EnsureFileExists(Path.Combine(definitionDirectory, "report.json"));
        EnsureFileExists(Path.Combine(pagesDirectory, "pages.json"));
        EnsureDirectoryExists(pagesDirectory);

        var report = new PbirReport
        {
            DefinitionPbir = ReadRequiredJson<DefinitionPbirFile>(Path.Combine(inputDirectory, "definition.pbir")),
            Platform = ReadRequiredJson<PlatformFile>(Path.Combine(inputDirectory, ".platform")),
            VersionMetadata = ReadRequiredJson<VersionMetadataRoot>(Path.Combine(definitionDirectory, "version.json")),
            Report = ReadRequiredJson<ReportRoot>(Path.Combine(definitionDirectory, "report.json")),
            PagesMetadata = ReadRequiredJson<PagesMetadataRoot>(Path.Combine(pagesDirectory, "pages.json"))
        };

        if (File.Exists(reportExtensionsPath))
            report.ReportExtension = ReadRequiredJson<ReportExtensionRoot>(reportExtensionsPath);

        foreach (var pageDir in Directory.GetDirectories(pagesDirectory))
        {
            var pageJsonPath = Path.Combine(pageDir, "page.json");
            if (!File.Exists(pageJsonPath))
                continue;

            var pageId = Path.GetFileName(pageDir);
            var pbirPage = new PbirPage
            {
                PageId = pageId,
                Page = ReadRequiredJson<PageRoot>(pageJsonPath)
            };

            var visualsDirectory = Path.Combine(pageDir, "visuals");
            if (Directory.Exists(visualsDirectory))
            {
                foreach (var visualDir in Directory.GetDirectories(visualsDirectory))
                {
                    var visualJsonPath = Path.Combine(visualDir, "visual.json");
                    if (!File.Exists(visualJsonPath))
                        continue;

                    pbirPage.Visuals.Add(new PbirVisual
                    {
                        VisualId = Path.GetFileName(visualDir),
                        Visual = ReadRequiredJson<VisualContainerRoot>(visualJsonPath)
                    });
                }
            }

            pbirPage.Visuals = pbirPage.Visuals
                .OrderBy(v => v.VisualId, StringComparer.Ordinal)
                .ToList();

            report.Pages.Add(pbirPage);
        }

        report.Pages = OrderPages(report.Pages, report.PagesMetadata.PageOrder);

        var bookmarksMetadataPath = Path.Combine(bookmarksDirectory, "bookmarks.json");
        if (File.Exists(bookmarksMetadataPath))
        {
            report.BookmarksMetadata = ReadRequiredJson<BookmarksMetadataRoot>(bookmarksMetadataPath);
            foreach (var bookmarkFile in Directory.GetFiles(bookmarksDirectory, "*.bookmark.json"))
            {
                var fileName = Path.GetFileName(bookmarkFile);
                var bookmarkId = fileName[..^".bookmark.json".Length];
                report.Bookmarks.Add(new PbirBookmark
                {
                    BookmarkId = bookmarkId,
                    Bookmark = ReadRequiredJson<BookmarkRoot>(bookmarkFile)
                });
            }

            report.Bookmarks = report.Bookmarks
                .OrderBy(b => b.BookmarkId, StringComparer.Ordinal)
                .ToList();
        }

        return report;
    }

    public static void Serialize(PbirReport report, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        var definitionDirectory = Path.Combine(outputDirectory, "definition");
        var pagesDirectory = Path.Combine(definitionDirectory, "pages");
        var bookmarksDirectory = Path.Combine(definitionDirectory, "bookmarks");

        Directory.CreateDirectory(definitionDirectory);
        Directory.CreateDirectory(pagesDirectory);

        WriteJson(Path.Combine(outputDirectory, "definition.pbir"), report.DefinitionPbir);
        WriteJson(Path.Combine(outputDirectory, ".platform"), report.Platform);
        WriteJson(Path.Combine(definitionDirectory, "version.json"), report.VersionMetadata);
        WriteJson(Path.Combine(definitionDirectory, "report.json"), report.Report);
        if (report.ReportExtension is not null)
            WriteJson(Path.Combine(definitionDirectory, "reportExtensions.json"), report.ReportExtension);
        WriteJson(Path.Combine(pagesDirectory, "pages.json"), report.PagesMetadata);

        foreach (var page in OrderPages(report.Pages, report.PagesMetadata.PageOrder))
        {
            var pageDirectory = Path.Combine(pagesDirectory, page.PageId);
            var visualsDirectory = Path.Combine(pageDirectory, "visuals");
            Directory.CreateDirectory(pageDirectory);
            Directory.CreateDirectory(visualsDirectory);

            WriteJson(Path.Combine(pageDirectory, "page.json"), page.Page);

            foreach (var visual in page.Visuals.OrderBy(v => v.VisualId, StringComparer.Ordinal))
            {
                var visualDirectory = Path.Combine(visualsDirectory, visual.VisualId);
                Directory.CreateDirectory(visualDirectory);
                WriteJson(Path.Combine(visualDirectory, "visual.json"), visual.Visual);
            }
        }

        if (report.BookmarksMetadata is not null || report.Bookmarks.Count > 0)
        {
            Directory.CreateDirectory(bookmarksDirectory);
            if (report.BookmarksMetadata is not null)
                WriteJson(Path.Combine(bookmarksDirectory, "bookmarks.json"), report.BookmarksMetadata);

            foreach (var bookmark in report.Bookmarks.OrderBy(b => b.BookmarkId, StringComparer.Ordinal))
            {
                WriteJson(Path.Combine(bookmarksDirectory, $"{bookmark.BookmarkId}.bookmark.json"), bookmark.Bookmark);
            }
        }
    }

    private static T ReadRequiredJson<T>(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var result = JsonConvert.DeserializeObject<T>(json, JsonSettings);
        if (result is null)
            throw new InvalidOperationException($"Unable to deserialize JSON file: {path}");
        return result;
    }

    private static void WriteJson<T>(string path, T content)
    {
        var json = JsonConvert.SerializeObject(content, Formatting.Indented, JsonSettings);
        File.WriteAllText(path, json, Utf8NoBom);
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required PBIR file not found: {path}");
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Required PBIR directory not found: {path}");
    }

    private static List<PbirPage> OrderPages(IEnumerable<PbirPage> pages, IEnumerable<string>? pageOrder)
    {
        var pageList = pages.ToList();
        var pageOrderList = pageOrder?.ToList() ?? [];
        if (pageOrderList.Count == 0)
            return pageList.OrderBy(p => p.PageId, StringComparer.Ordinal).ToList();

        var rankMap = pageOrderList
            .Select((id, idx) => new { id, idx })
            .ToDictionary(x => x.id, x => x.idx, StringComparer.Ordinal);

        return pageList
            .OrderBy(p => rankMap.TryGetValue(p.PageId, out var idx) ? idx : int.MaxValue)
            .ThenBy(p => p.PageId, StringComparer.Ordinal)
            .ToList();
    }
}
