using OpenBI.Interfaces.Infrastructure;
using OpenBI.Converters.Interfaces;
using OpenBI.Converters.PowerBI.Models.msSchemas;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Page;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.PagesMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.Report;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.ReportExtension;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualConfiguration;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VersionMetadata;
using OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualContainer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBI;
using OpenBI.Patching;
using OpenBI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VC = OpenBI.Converters.PowerBI.Models.msSchemas.Models.VisualConfiguration;

namespace OpenBI.Converters.PowerBI.Converters
{
    public class PowerBIReportPbirOpenBIConverter : IOpenBIConverter
    {
        private const string DefaultReportName = "Report";
        private const string ReportFolderSuffix = ".Report/";
        private const string MetadataObjectsKey = "objects";
        private const string MetadataVisualContainerObjectsKey = "visualContainerObjects";

        private IArtifactCompressionService? _compression;
        private ILogger? _logger;

        public PowerBIReportPbirOpenBIConverter(ILogger<PowerBIReportPbirOpenBIConverter> logger, IArtifactCompressionService compressionService)
        {
            _logger = logger;
            _compression = compressionService;
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void SetCompressionService(IArtifactCompressionService compressionService)
        {
            _compression = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
        }

        private static Expression? MakeExpression(string? code, string? language = null, string? type = null)
            => string.IsNullOrEmpty(code) ? null
               : new Expression { Id = Guid.NewGuid().ToString(), Code = code, Language = language, Type = type };

        private static string? GetCode(Expression? expr) => expr?.Code;

        public async Task<Asset> FromArtifactToOpenBIAsync(byte[] artifact)
        {
            EnsureCompressionService();
            if (artifact == null || artifact.Length == 0)
                throw new ArgumentNullException(nameof(artifact));

            _logger?.LogInformation("Converting PBIR artifact to OpenBI asset (size={Size})", artifact.Length);

            if (!TryFindPbirRootInZip(artifact, out _, out _))
            {
                _logger?.LogInformation("PBIR structure not found in artifact; legacy report.json is not supported in OpenBI.");
                return await ReadLegacyReportAsync(artifact).ConfigureAwait(false);
            }

            var tempRoot = CreateTempDirectory();
            try
            {
                var reportName = ExtractPbirToTemp(artifact, tempRoot);
                var pbir = PbirReportSerializer.Deserialize(tempRoot);
                var asset = MapToAsset(pbir, reportName, _logger);
                _logger?.LogInformation("Converted PBIR artifact to OpenBI asset: {AssetName}, pages={Pages}", asset.Info?.Name, asset.Layout?.Pages?.Count ?? 0);
                return asset;
            }
            finally
            {
                SafeDeleteDirectory(tempRoot);
            }
        }

        public async Task<byte[]> FromOpenBIToArtifactAsync(Asset asset)
        {
            EnsureCompressionService();

            ValidateAsset(asset);

            _logger?.LogInformation("Converting OpenBI report to PBIR artifact: {AssetName}", asset.Info?.Name);

            var tempRoot = CreateTempDirectory();
            try
            {
                var pbir = CreatePbirFromAsset(asset);
                PbirReportSerializer.Serialize(pbir, tempRoot);
                var reportName = NormalizeReportName(asset.Info?.Name);
                var rawZip = BuildPbirZipBytes(tempRoot, reportName);
                //var compressed = await _compression!.ZipAsync(rawZip).ConfigureAwait(false);
                _logger?.LogInformation("Converted OpenBI report to PBIR artifact: {AssetName}, size={Size}", reportName, rawZip.Length);
                return rawZip;
            }
            finally
            {
                SafeDeleteDirectory(tempRoot);
            }
        }

        public Task<OpenBIPatchResult> FromOpenBIPatchArtifactAsync(IEnumerable<OpenBIChange> changes, byte[] artifact)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (artifact == null || artifact.Length == 0) throw new ArgumentNullException(nameof(artifact));

            if (!TryFindPbirRootInZip(artifact, out var prefix, out _))
                throw new NotSupportedException(
                    "Patch is only supported for PBIR format. The provided artifact does not contain a PBIR definition.pbir entry.");

            var entries  = LoadAllZipEntries(artifact);
            var dirty    = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var errors   = new List<OpenBIPatchError>();
            var warnings = new List<OpenBIPatchError>();

            foreach (var change in changes)
            {
                try { ApplyPbirChange(entries, dirty, prefix, change, errors, warnings); }
                catch (Exception ex) { errors.Add(MakePatchError(change, $"Unexpected error: {ex.Message}", ex)); }
            }

            FlushDirty(dirty, entries);
            return Task.FromResult(new OpenBIPatchResult
            {
                Artifact = BuildZipFromEntries(entries),
                Errors   = errors,
                Warnings = warnings
            });
        }

        private static Asset MapToAsset(PbirReport report, string reportName, ILogger? logger)
        {
            var pages = report.Pages
                .Select((p, idx) => new Page
                {
                    Id = p.PageId,
                    EmbedPageUrlParameter = p.PageId,
                    Name = p.Page?.DisplayName ?? p.PageId,
                    Order = idx,
                    Width = ToDecimal(p.Page?.Width, 1280m),
                    Height = ToDecimal(p.Page?.Height, 720m),
                    IsEnabled = !string.Equals(p.Page?.Visibility, "HiddenInViewMode", StringComparison.OrdinalIgnoreCase),
                    Description = p.Page?.DisplayName,
                    Visuals = p.Visuals.Select(MapToVisual).ToList()
                })
                .ToList();

            var info = new AssetInfo
            {
                Id = reportName,
                Name = report.Platform?.Metadata?.DisplayName ?? reportName,
                Description = report.Platform?.Metadata?.DisplayName ?? reportName,
                Type = AssetType.Report,
                ExternalType = "Report"
            };

            var connectionString = report.DefinitionPbir?.DatasetReference?.ByConnection?.ConnectionString;
            if (!string.IsNullOrEmpty(connectionString))
            {
                if (DatasetReferenceByConnection.TryParseSemanticModelBinding(connectionString, out var workspaceName, out var semanticModelName, out var semanticModelId))
                {
                    info.AdditionalMetadata = new List<AdditionalMetadata>
                    {
                        new() { Name = "semantic_model_folder_name", Value = workspaceName },
                        new() { Name = "semantic_model_name", Value = semanticModelName },
                        new() { Name = "semantic_model_id", Value = semanticModelId }
                    };
                }
                else
                {
                    logger?.LogDebug("PBIR dataset connection string could not be parsed for semantic binding metadata.");
                }
            }

            return new Asset
            {
                Info = info,
                Layout = new Layout { Pages = pages },
                DataModel = new DataModel { Tables = MapReportExtensionsToOpenBiTables(report.ReportExtension), Relationships = new List<Relationship>() },
                Dependencies = Array.Empty<Asset>()
            };
        }

        private static Visual MapToVisual(PbirVisual visual)
        {
            var type = GetVisualType(visual.Visual?.Visual) ?? "visualContainer";
            var metadata = ExtractVisualAdditionalMetadata(visual.Visual?.Visual);
            return new Visual
            {
                Id = visual.VisualId,
                Name = visual.Visual?.Name ?? visual.VisualId,
                Type = type,
                X = ToDecimal(visual.Visual?.Position?.X, 0),
                Y = ToDecimal(visual.Visual?.Position?.Y, 0),
                Z = ToDecimal(visual.Visual?.Position?.Z, 0),
                Width = ToDecimal(visual.Visual?.Position?.Width, 240),
                Height = ToDecimal(visual.Visual?.Position?.Height, 160),
                VisualProjections = ExtractVisualProjections(visual.Visual?.Visual),
                AdditionalMetadata = metadata,
                VisualLevelFilters = new List<OpenBI.Filter>()
            };
        }

        private static ICollection<VisualProjection> ExtractVisualProjections(VC.VisualConfigurationRoot? visualConfiguration)
        {
            var visualToken = BuildVisualToken(visualConfiguration);
            if (visualToken is null)
                return new List<VisualProjection>();

            var projections = new List<VisualProjection>();

            // Legacy shape (singleVisual.projections + prototypeQuery.Select).
            var legacyProjectionBuckets = visualToken["singleVisual"]?["projections"] as JObject;
            var legacySelectArray = visualToken["singleVisual"]?["prototypeQuery"]?["Select"] as JArray;
            if (legacyProjectionBuckets is not null)
            {
                foreach (var projectionBucket in legacyProjectionBuckets.Properties())
                {
                    var projIndex = 1;
                    var bucketValues = projectionBucket.Value as JArray;
                    if (bucketValues is null)
                        continue;

                    foreach (var projVal in bucketValues.Children<JObject>())
                    {
                        var queryRef = projVal["queryRef"]?.ToString();
                        var selectStatement = legacySelectArray?.Children<JObject>()
                            .SingleOrDefault(x => string.Equals(x["Name"]?.ToString(), queryRef, StringComparison.Ordinal));

                        var isMeasure = selectStatement?["Measure"] != null || selectStatement?["Aggregation"] != null;
                        var isDimension = selectStatement?["Column"] != null || selectStatement?["HierarchyLevel"] != null;
                        var isAggregation = selectStatement?["Aggregation"] != null;

                        if (selectStatement?[""] != null)
                        {
                            //NOT IMPLEMENTED!
                            continue;
                        }

                        projections.Add(new VisualProjection
                        {
                            ProjectionName = projectionBucket.Name,
                            IdColumnReference = !isAggregation ? queryRef : null,
                            Expression = isAggregation ? MakeExpression(queryRef, "DAX", "Implicit Measure") : null,
                            Order = projIndex++,
                            IsDimension = isDimension,
                            IsMeasure = isMeasure
                        });
                    }
                }
            }

            // PBIR shape (query.queryState.<Bucket>.projections).
            var queryState = visualToken["query"]?["queryState"] as JObject;
            if (queryState is not null)
            {
                foreach (var bucket in queryState.Properties())
                {
                    var bucketProjections = bucket.Value?["projections"] as JArray;
                    if (bucketProjections is null)
                        continue;

                    var projIndex = 1;
                    foreach (var projVal in bucketProjections.Children<JObject>())
                    {
                        var field = projVal["field"] as JObject;
                        var isAggregation = field?["Aggregation"] != null;
                        var isMeasure = field?["Measure"] != null || isAggregation;
                        var isDimension = field?["Column"] != null || field?["HierarchyLevel"] != null;
                        var queryRef = projVal["queryRef"]?.ToString();

                        projections.Add(new VisualProjection
                        {
                            ProjectionName = bucket.Name,
                            IdColumnReference = !isAggregation ? queryRef : null,
                            Expression = isAggregation ? MakeExpression(queryRef, "DAX", "Implicit Measure") : null,
                            Order = projIndex++,
                            IsDimension = isDimension,
                            IsMeasure = isMeasure
                        });
                    }
                }
            }

            return projections;
        }

        private static ICollection<AdditionalMetadata> ExtractVisualAdditionalMetadata(VC.VisualConfigurationRoot? visualConfiguration)
        {
            var visualToken = BuildVisualToken(visualConfiguration);
            if (visualToken is null)
                return GetOrCreateAdditionalMetadata(null);

            var metadata = GetOrCreateAdditionalMetadata(null);
            if (visualToken.TryGetValue(MetadataObjectsKey, StringComparison.Ordinal, out var objectsToken))
                UpsertAdditionalMetadata(metadata, MetadataObjectsKey, objectsToken.ToString(Formatting.None));
            if (visualToken.TryGetValue(MetadataVisualContainerObjectsKey, StringComparison.Ordinal, out var visualContainerObjectsToken))
                UpsertAdditionalMetadata(metadata, MetadataVisualContainerObjectsKey, visualContainerObjectsToken.ToString(Formatting.None));
            return metadata;
        }

        private static ICollection<AdditionalMetadata> GetOrCreateAdditionalMetadata(ICollection<AdditionalMetadata>? metadata)
        {
            return metadata ?? new List<AdditionalMetadata>();
        }

        private static void UpsertAdditionalMetadata(ICollection<AdditionalMetadata> metadata, string key, string rawJson)
        {
            var existing = metadata.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.Ordinal));
            if (existing is null)
            {
                metadata.Add(new AdditionalMetadata
                {
                    Name = key,
                    Value = rawJson
                });
                return;
            }

            existing.Value = rawJson;
        }

        private static bool TryGetAdditionalMetadataJson(Visual visual, string key, out string rawJson)
        {
            rawJson = string.Empty;
            var entry = visual.AdditionalMetadata?.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.Ordinal));
            if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                return false;

            rawJson = entry.Value;
            return true;
        }

        private static bool TryParseRawJsonValue(string rawJson, out JToken parsed)
        {
            parsed = JValue.CreateNull();
            try
            {
                parsed = JToken.Parse(rawJson);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static JObject? BuildVisualToken(VC.VisualConfigurationRoot? visualConfiguration)
        {
            if (visualConfiguration is null)
                return null;

            try
            {
                var json = JsonConvert.SerializeObject(
                    visualConfiguration,
                    Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static PbirReport CreatePbirFromAsset(Asset asset)
        {
            var reportName = NormalizeReportName(asset.Info?.Name);
            var pbir = CreateDefaultPbir(asset);
            ApplyAssetToPbir(pbir, asset);
            return pbir;
        }

        private static void ApplyAssetToPbir(PbirReport pbir, Asset asset)
        {
            var extensionKeys = BuildExtensionMeasureKeys(asset.DataModel?.Tables);
            var extensionSchemaName = string.IsNullOrWhiteSpace(pbir.ReportExtension?.Name)
                ? "extension"
                : pbir.ReportExtension!.Name;

            var pages = (asset.Layout?.Pages ?? new List<Page>())
                .OrderBy(p => p.Order)
                .ToList();

            pbir.Pages = pages.Select((page, pageIndex) =>
            {
                var pageId = ToPbirObjectId(page.Id, $"page-{pageIndex + 1}");
                var pbirPage = new PbirPage
                {
                    PageId = pageId,
                    Page = new PageRoot
                    {
                        Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.0.0/schema.json",
                        Name = pageId,
                        DisplayName = string.IsNullOrWhiteSpace(page.Name) ? pageId : page.Name,
                        DisplayOption = "FitToPage",
                        Width = (double)page.Width,
                        Height = (double)page.Height
                    },
                    Visuals = (page.Visuals ?? Array.Empty<Visual>())
                        .Select((v, visualIndex) => new PbirVisual
                        {
                            VisualId = ToPbirObjectId(v.Id, $"visual-{pageId}-{visualIndex + 1}"),
                            Visual = new VisualContainerRoot
                            {
                                Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.2.0/schema.json",
                                Name = ToPbirObjectId(v.Id, $"visual-{pageId}-{visualIndex + 1}"),
                                Position = new VisualContainerPosition
                                {
                                    X = (double)v.X,
                                    Y = (double)v.Y,
                                    Z = (double)v.Z,
                                    Width = (double)v.Width,
                                    Height = (double)v.Height
                                },
                                Visual = BuildVisualConfigurationFromOpenBi(v, extensionKeys, extensionSchemaName)
                            }
                        })
                        .ToList()
                };

                return pbirPage;
            }).ToList();

            pbir.PagesMetadata.PageOrder = pbir.Pages.Select(p => p.PageId).ToList();
            pbir.PagesMetadata.ActivePageName = pbir.PagesMetadata.PageOrder.FirstOrDefault() ?? string.Empty;
            pbir.Platform.Metadata ??= new PlatformMetadata();
            pbir.Platform.Metadata.DisplayName = NormalizeReportName(asset.Info?.Name);
            pbir.ReportExtension = MapOpenBiTablesToReportExtension(asset.DataModel?.Tables, pbir.ReportExtension?.Name);
        }

        private static List<Table> MapReportExtensionsToOpenBiTables(ReportExtensionRoot? reportExtension)
        {
            if (reportExtension?.Entities == null)
                return new List<Table>();

            var tables = new List<Table>();
            foreach (var entity in reportExtension.Entities)
            {
                if (entity == null || string.IsNullOrWhiteSpace(entity.Name))
                    continue;

                var table = new Table
                {
                    Type = Table.TableTypeObject,
                    Id   = entity.Name,
                    Name = entity.Name,
                    Columns = new List<Column>()
                };

                foreach (var measure in entity.Measures ?? Array.Empty<ReportExtensionMeasure>())
                {
                    if (measure == null || string.IsNullOrWhiteSpace(measure.Name) || string.IsNullOrWhiteSpace(measure.Expression))
                        continue;

                    table.Columns.Add(new Column
                    {
                        Type = "measure",
                        Id   = measure.Name,
                        Name = measure.Name,
                        Description = string.IsNullOrWhiteSpace(measure.Description) ? null : measure.Description,
                        Expression = MakeExpression(measure.Expression, null, "Measure"),
                        IsMeasure = true,
                        DataType = MapReportExtensionPrimitiveTypeToOpenBi(measure.DataType)
                    });
                }

                tables.Add(table);
            }

            return tables;
        }

        private static ReportExtensionRoot? MapOpenBiTablesToReportExtension(IEnumerable<Table>? tables, string? existingExtensionName)
        {
            var mappedEntities = new List<ReportExtensionEntity>();
            foreach (var table in tables ?? Array.Empty<Table>())
            {
                if (table == null || !string.Equals(table.Type, Table.TableTypeObject, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(table.Name))
                    continue;

                var measures = new List<ReportExtensionMeasure>();
                foreach (var column in table.Columns ?? Array.Empty<Column>())
                {
                    if (column == null || !IsMeasureColumn(column))
                        continue;
                    if (string.IsNullOrWhiteSpace(column.Name) || string.IsNullOrWhiteSpace(GetCode(column.Expression)))
                        continue;

                    var mappedMeasure = new ReportExtensionMeasure
                    {
                        Name = column.Name,
                        Expression = GetCode(column.Expression),
                        DataType = MapOpenBiDataTypeToReportExtensionPrimitiveType(column.DataType)
                    };
                    if (!string.IsNullOrWhiteSpace(column.Description))
                        mappedMeasure.Description = column.Description;

                    measures.Add(mappedMeasure);
                }

                mappedEntities.Add(new ReportExtensionEntity
                {
                    Name = table.Name,
                    Measures = measures
                });
            }

            var extensionName = string.IsNullOrWhiteSpace(existingExtensionName) ? "extension" : existingExtensionName;
            return new ReportExtensionRoot
            {
                Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/reportExtension/1.0.0/schema.json",
                Name = extensionName,
                Entities = mappedEntities
            };
        }

        private static bool IsMeasureColumn(Column column) =>
            string.Equals(column.Type, "measure", StringComparison.OrdinalIgnoreCase);

        private static HashSet<string> BuildExtensionMeasureKeys(IEnumerable<Table>? tables)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables ?? Array.Empty<Table>())
            {
                if (table == null || !string.Equals(table.Type, Table.TableTypeObject, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(table.Name))
                    continue;

                foreach (var column in table.Columns ?? Array.Empty<Column>())
                {
                    if (column == null || !IsMeasureColumn(column) || string.IsNullOrWhiteSpace(column.Name))
                        continue;

                    keys.Add($"{table.Name}.{column.Name}");
                }
            }

            return keys;
        }

        private static (HashSet<string> Keys, string SchemaName) GetExtensionMeasureKeysFromReportExt(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var schemaName = "extension";
            var jo = GetOrLoad(entries, dirty, ReportExtPath(prefix));
            if (jo == null)
                return (keys, schemaName);

            var name = jo["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                schemaName = name;

            foreach (var entity in (jo["entities"] as JArray ?? new JArray()).Children<JObject>())
            {
                var entityName = entity["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(entityName))
                    continue;

                foreach (var measure in (entity["measures"] as JArray ?? new JArray()).Children<JObject>())
                {
                    var measureName = measure["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(measureName))
                        continue;

                    keys.Add($"{entityName}.{measureName}");
                }
            }

            return (keys, schemaName);
        }

        private static JObject BuildSourceRefJObject(string entity, string? schema)
        {
            var sourceRef = new JObject { ["Entity"] = entity };
            if (!string.IsNullOrWhiteSpace(schema))
                sourceRef["Schema"] = schema;
            return sourceRef;
        }

        private static bool IsExtensionMeasure(string queryRef, bool isMeasure, HashSet<string> extensionKeys)
        {
            if (!isMeasure || extensionKeys.Count == 0)
                return false;

            var (entity, property) = ParseEntityProperty(queryRef);
            if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(property))
                return false;

            return extensionKeys.Contains($"{entity}.{property}");
        }

        private static PrimitiveTypeName MapOpenBiDataTypeToReportExtensionPrimitiveType(Column.ColumnDataType dataType)
        {
            return dataType switch
            {
                Column.ColumnDataType.String => PrimitiveTypeName.Text,
                Column.ColumnDataType.Integer => PrimitiveTypeName.Integer,
                Column.ColumnDataType.Decimal => PrimitiveTypeName.Double,
                Column.ColumnDataType.Date => PrimitiveTypeName.DateTime,
                Column.ColumnDataType.Timestamp => PrimitiveTypeName.DateTime,
                Column.ColumnDataType.Boolean => PrimitiveTypeName.Boolean,
                Column.ColumnDataType.Time => PrimitiveTypeName.Time,
                _ => PrimitiveTypeName.None
            };
        }

        private static Column.ColumnDataType MapReportExtensionPrimitiveTypeToOpenBi(PrimitiveTypeName dataType)
        {
            return dataType switch
            {
                PrimitiveTypeName.Text => Column.ColumnDataType.String,
                PrimitiveTypeName.Integer => Column.ColumnDataType.Integer,
                PrimitiveTypeName.Decimal => Column.ColumnDataType.Decimal,
                PrimitiveTypeName.Double => Column.ColumnDataType.Decimal,
                PrimitiveTypeName.Date => Column.ColumnDataType.Date,
                PrimitiveTypeName.DateTime => Column.ColumnDataType.Timestamp,
                PrimitiveTypeName.DateTimeZone => Column.ColumnDataType.Timestamp,
                PrimitiveTypeName.Boolean => Column.ColumnDataType.Boolean,
                PrimitiveTypeName.Time => Column.ColumnDataType.Time,
                _ => Column.ColumnDataType.Unknown
            };
        }

        private static VC.VisualConfigurationRoot BuildVisualConfigurationFromOpenBi(
            Visual visual,
            HashSet<string> extensionKeys,
            string extensionSchemaName)
        {
            var baseConfig = new VC.VisualConfigurationRoot
            {
                VisualType = string.IsNullOrWhiteSpace(visual.Type) ? "visualContainer" : visual.Type,
                DrillFilterOtherVisuals = true
            };

            var query = BuildTypedQueryFromVisualProjections(
                visual.VisualProjections ?? Array.Empty<VisualProjection>(),
                extensionKeys,
                extensionSchemaName);
            if (query is not null)
                baseConfig.Query = query;

            var configToken = JObject.FromObject(baseConfig, JsonSerializer.CreateDefault(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }));

            if (TryGetAdditionalMetadataJson(visual, MetadataObjectsKey, out var objectsRawJson) &&
                TryParseRawJsonValue(objectsRawJson, out var objectsToken))
            {
                configToken[MetadataObjectsKey] = objectsToken;
            }

            if (TryGetAdditionalMetadataJson(visual, MetadataVisualContainerObjectsKey, out var visualContainerObjectsRawJson) &&
                TryParseRawJsonValue(visualContainerObjectsRawJson, out var visualContainerObjectsToken))
            {
                configToken[MetadataVisualContainerObjectsKey] = visualContainerObjectsToken;
            }

            return configToken.ToObject<VC.VisualConfigurationRoot>() ?? baseConfig;
        }

        private static VC.Query? BuildTypedQueryFromVisualProjections(
            IEnumerable<VisualProjection> projections,
            HashSet<string> extensionKeys,
            string extensionSchemaName)
        {
            var kept = GetKeptProjectionsForWrite(projections).ToList();
            if (kept.Count == 0)
                return null;

            var queryState = new Dictionary<string, VC.ProjectionState>();
            foreach (var projectionGroup in kept.GroupBy(p => p.ProjectionName))
            {
                if (string.IsNullOrWhiteSpace(projectionGroup.Key))
                    continue;

                var mappedProjections = new List<VC.RoleProjection>();
                foreach (var projection in projectionGroup.OrderBy(p => p.Order))
                {
                    var queryRef = ResolveQueryReference(projection);
                    if (string.IsNullOrWhiteSpace(queryRef))
                        continue;

                    mappedProjections.Add(new VC.RoleProjection
                    {
                        Field = BuildFieldFromProjection(projection, queryRef, extensionKeys, extensionSchemaName),
                        QueryRef = queryRef,
                        NativeQueryRef = BuildNativeQueryRef(queryRef),
                        Active = projection.IsActive
                    });
                }

                if (mappedProjections.Count == 0)
                    continue;

                queryState[projectionGroup.Key] = new VC.ProjectionState
                {
                    Projections = mappedProjections
                };
            }

            return queryState.Count == 0
                ? null
                : new VC.Query
                {
                    QueryState = queryState
                };
        }

        private static IEnumerable<VisualProjection> GetKeptProjectionsForWrite(IEnumerable<VisualProjection> projections)
        {
            foreach (var projection in projections)
            {
                if (projection is null)
                    continue;

                // Keep parity with legacy writer: skip aggregated measures represented only by expression.
                if (projection.IsMeasure && projection.IdColumnReference == null && !string.IsNullOrWhiteSpace(GetCode(projection.Expression)))
                    continue;

                if (!projection.IsMeasure && !projection.IsDimension)
                    continue;

                yield return projection;
            }
        }

        private static string? ResolveQueryReference(VisualProjection projection)
        {
            if (!string.IsNullOrWhiteSpace(projection.IdColumnReference))
                return projection.IdColumnReference;

            if (!string.IsNullOrWhiteSpace(GetCode(projection.Expression)))
                return GetCode(projection.Expression);

            return null;
        }

        private static VC.Field BuildFieldFromProjection(
            VisualProjection projection,
            string queryRef,
            HashSet<string> extensionKeys,
            string extensionSchemaName)
        {
            var (entity, property) = ParseEntityProperty(queryRef);
            if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(property))
            {
                // Fallback: keep a minimal legal shape to preserve projection data.
                return new VC.Field
                {
                    AdditionalProperties = new Dictionary<string, object?>
                    {
                        ["Column"] = new JObject
                        {
                            ["Expression"] = new JObject
                            {
                                ["SourceRef"] = new JObject { ["Entity"] = "Unknown" }
                            },
                            ["Property"] = queryRef
                        }
                    }
                };
            }

            var measureSchema = IsExtensionMeasure(queryRef, projection.IsMeasure, extensionKeys)
                ? extensionSchemaName
                : null;

            var column = new JObject
            {
                ["Expression"] = new JObject
                {
                    ["SourceRef"] = BuildSourceRefJObject(entity, null)
                },
                ["Property"] = property
            };

            if (projection.IsMeasure)
            {
                return new VC.Field
                {
                    AdditionalProperties = new Dictionary<string, object?>
                    {
                        ["Measure"] = new JObject
                        {
                            ["Expression"] = new JObject { ["SourceRef"] = BuildSourceRefJObject(entity, measureSchema) },
                            ["Property"] = property
                        }
                    }
                };
            }

            return new VC.Field
            {
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["Column"] = column
                }
            };
        }

        private static string BuildNativeQueryRef(string queryRef)
        {
            var parts = queryRef.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length >= 2 ? parts[^1] : queryRef;
        }

        private static (string entity, string property) ParseEntityProperty(string queryRef)
        {
            var value = queryRef.Trim();
            if (value.StartsWith("Sum(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
                value = value[4..^1];
            else if (value.StartsWith("Avg(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
                value = value[4..^1];
            else if (value.StartsWith("Min(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
                value = value[4..^1];
            else if (value.StartsWith("Max(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
                value = value[4..^1];
            else if (value.StartsWith("Count(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
                value = value[6..^1];

            var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                return (string.Empty, string.Empty);

            return (parts[0], string.Join('.', parts.Skip(1)));
        }

        private static PbirReport CreateDefaultPbir(Asset asset)
        {
            var meta = asset.Info.AdditionalMetadata;
            string semantic_model_folder_name = meta?.SingleOrDefault(x => x.Name == "semantic_model_folder_name")?.Value ?? string.Empty;
            string semantic_model_folder_id = meta?.SingleOrDefault(x => x.Name == "semantic_model_folder_id")?.Value ?? string.Empty;
            string semantic_model_id = meta?.SingleOrDefault(x => x.Name == "semantic_model_id")?.Value ?? string.Empty;
            string semantic_model_name = meta?.SingleOrDefault(x => x.Name == "semantic_model_name")?.Value ?? string.Empty;

            if (string.IsNullOrEmpty(semantic_model_folder_name) ||
                string.IsNullOrEmpty(semantic_model_id) ||
                string.IsNullOrEmpty(semantic_model_name))
                throw new InvalidOperationException("Additional parameters on asset required: semantic_model_folder_name; semantic_model_id; semantic_model_name");

            return new PbirReport
            {
                DefinitionPbir = new DefinitionPbirFile
                {
                    Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json",
                    Version = "4.0",
                    DatasetReference = new DatasetReference
                    {
                        ByConnection = new DatasetReferenceByConnection()
                        .WithConnectionString(
                            workspaceId: semantic_model_folder_id,
                            workspaceName: semantic_model_folder_name,
                            semanticModelName: semantic_model_name,
                            semanticModelId: semantic_model_id)
                    }
                },
                Platform = new PlatformFile
                {
                    Schema = "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json",
                    Metadata = new PlatformMetadata
                    {
                        Type = "Report",
                        DisplayName = asset.Info.Name
                    },
                    Config = new PlatformConfig
                    {
                        Version = "2.0",
                        LogicalId = Guid.NewGuid().ToString()
                    }
                },
                VersionMetadata = new VersionMetadataRoot
                {
                    Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/versionMetadata/1.0.0/schema.json",
                    Version = "2.0.0"
                },
                Report = new ReportRoot
                {
                    Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/report/3.0.0/schema.json",
                    ThemeCollection = new ThemeCollection
                    {
                        BaseTheme = new ThemeMetadata
                        {
                            Name = "CY24SU04",
                            Type = "SharedResources",
                            ReportVersionAtImport = new ThemeVersion()
                            {
                                Page = "1.3.91",
                                Report = "2.0.91",
                                Visual = "1.8.91"
                            }
                        }
                    },
                    
                },
                PagesMetadata = new PagesMetadataRoot
                {
                    Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.0.0/schema.json",
                    PageOrder = new List<string>(),
                    ActivePageName = string.Empty
                }
            };
        }

        private static byte[] BuildPbirZipBytes(string sourceDirectory, string reportName)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var prefix = $"{NormalizeReportName(reportName)}{ReportFolderSuffix}";
                AddFileToZip(zip, Path.Combine(sourceDirectory, ".platform"), $".platform");
                AddFileToZip(zip, Path.Combine(sourceDirectory, "definition.pbir"), $"definition.pbir");
                AddDirectoryToZip(zip, Path.Combine(sourceDirectory, "definition"), $"definition");
            }

            ms.Position = 0;
            return ms.ToArray();
        }

        private static void AddDirectoryToZip(ZipArchive zip, string folderPath, string zipFolderPath)
        {
            foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
                AddFileToZip(zip, file, $"{zipFolderPath}/{relative}");
            }
        }

        private static void AddFileToZip(ZipArchive zip, string filePath, string entryPath)
        {
            var entry = zip.CreateEntry(entryPath.Replace('\\', '/'));
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(filePath);
            fileStream.CopyTo(entryStream);
        }

        private static string ExtractPbirToTemp(byte[] zipBytes, string tempRoot)
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
            if (!TryFindPbirRootInArchive(zip, out var reportFolderPrefix, out var reportName))
                throw new FileNotFoundException("No PBIR definition.pbir entry found in the provided ZIP.");

            var entries = zip.Entries
                .Where(e => e.FullName.StartsWith(reportFolderPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var relative = entry.FullName.Substring(reportFolderPrefix.Length).Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = Path.Combine(tempRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                using var input = entry.Open();
                using var output = File.Create(destinationPath);
                input.CopyTo(output);
            }

            return reportName;
        }

        private static bool TryFindPbirRootInZip(byte[] zipBytes, out string reportFolderPrefix, out string reportName)
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
            return TryFindPbirRootInArchive(zip, out reportFolderPrefix, out reportName);
        }

        private static bool TryFindPbirRootInArchive(ZipArchive zip, out string reportFolderPrefix, out string reportName)
        {
            var definitionEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith($"{ReportFolderSuffix}definition.pbir", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("definition.pbir", StringComparison.OrdinalIgnoreCase));

            if (definitionEntry == null)
            {
                reportFolderPrefix = string.Empty;
                reportName = DefaultReportName;
                return false;
            }

            var definitionReportJson = zip.Entries.SingleOrDefault(x => x.FullName == "definition/report.json");

            if (definitionReportJson != null)
            {
                reportFolderPrefix = string.Empty;
                reportName = DefaultReportName;
                
                return true;
            }

            reportFolderPrefix = string.Empty;
            reportName = DefaultReportName;
            return false;
        }

        private static string? GetVisualType(VC.VisualConfigurationRoot? visualConfiguration)
        {
            return visualConfiguration?.VisualType;
        }

        private static decimal ToDecimal(double? value, decimal fallback) => value.HasValue ? (decimal)value.Value : fallback;

        private static string NormalizeReportName(string? value) =>
            string.IsNullOrWhiteSpace(value) ? DefaultReportName : value.Trim();

        // PBIR object names/folder IDs use lowercase compact hex identifiers (e.g. 0f188350c0f0bf049339).
        private static string ToPbirObjectId(string? candidateId, string fallbackSeed)
        {
            if (!string.IsNullOrWhiteSpace(candidateId))
            {
                var normalized = candidateId.Trim().ToLowerInvariant();
                if (normalized.Length == 20 && normalized.All(IsHexChar))
                    return normalized;

                if (Guid.TryParse(normalized, out var parsedGuid))
                    return parsedGuid.ToString("N", CultureInfo.InvariantCulture)[..20];
            }

            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(fallbackSeed));
            return string.Concat(hash.Take(10).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static bool IsHexChar(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"openbi_pbir_converter_{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        private static void ValidateAsset(Asset asset)
        {
            if (asset?.Info == null)
                throw new ArgumentNullException(nameof(asset));
        }

        private void EnsureCompressionService()
        {
            if (_compression == null)
                throw new InvalidOperationException("Compression service has not been set. Call SetCompressionService before using conversion methods that require compression.");
        }

        // ── Patch: ZIP infrastructure ─────────────────────────────────────────────

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static Dictionary<string, byte[]> LoadAllZipEntries(byte[] artifact)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var zip = new ZipArchive(new MemoryStream(artifact), ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                using var ms = new MemoryStream();
                using var stream = entry.Open();
                stream.CopyTo(ms);
                result[entry.FullName] = ms.ToArray();
            }
            return result;
        }

        private static JObject? GetOrLoad(Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty, string path)
        {
            if (dirty.TryGetValue(path, out var cached)) return cached;
            if (!entries.TryGetValue(path, out var bytes)) return null;
            var jo = JObject.Parse(Encoding.UTF8.GetString(bytes));
            dirty[path] = jo;
            return jo;
        }

        private static JObject GetOrLoadOrCreate(Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty, string path, Func<JObject> factory)
        {
            var existing = GetOrLoad(entries, dirty, path);
            if (existing != null) return existing;
            var created = factory();
            dirty[path] = created;
            entries[path] = Utf8NoBom.GetBytes(created.ToString(Formatting.Indented));
            return created;
        }

        private static void FlushDirty(Dictionary<string, JObject> dirty, Dictionary<string, byte[]> entries)
        {
            foreach (var kv in dirty)
                entries[kv.Key] = Utf8NoBom.GetBytes(kv.Value.ToString(Formatting.Indented));
        }

        private static byte[] BuildZipFromEntries(Dictionary<string, byte[]> entries)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var kv in entries)
                {
                    var entry = zip.CreateEntry(kv.Key.Replace('\\', '/'));
                    using var entryStream = entry.Open();
                    entryStream.Write(kv.Value, 0, kv.Value.Length);
                }
            }
            ms.Position = 0;
            return ms.ToArray();
        }

        // ── Patch: path helpers ───────────────────────────────────────────────────

        private static string PagesJsonPath(string prefix)
            => $"{prefix}definition/pages/pages.json";
        private static string PageJsonPath(string prefix, string pageId)
            => $"{prefix}definition/pages/{pageId}/page.json";
        private static string VisualJsonPath(string prefix, string pageId, string visualId)
            => $"{prefix}definition/pages/{pageId}/visuals/{visualId}/visual.json";
        private static string ReportExtPath(string prefix)
            => $"{prefix}definition/reportExtensions.json";
        private static string PlatformPath(string prefix)
            => $"{prefix}.platform";

        private static (string? pageId, string? entryPath) FindVisual(
            Dictionary<string, byte[]> entries, string prefix, string visualId)
        {
            var pagesRoot    = $"{prefix}definition/pages/";
            var visualSuffix = $"/visuals/{visualId}/visual.json";
            foreach (var key in entries.Keys)
            {
                if (!key.StartsWith(pagesRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.EndsWith(visualSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                var after = key.Substring(pagesRoot.Length);
                var slash = after.IndexOf('/');
                if (slash < 0) continue;
                return (after[..slash], key);
            }
            return (null, null);
        }

        // ── Patch: dispatch ───────────────────────────────────────────────────────

        private static void ApplyPbirChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            switch (change.Entity)
            {
                case OpenBIEntity.AssetInfo:
                    ApplyAssetInfoChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                case OpenBIEntity.Page:
                    ApplyPageChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                case OpenBIEntity.Visual:
                    ApplyVisualChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                case OpenBIEntity.VisualProjection:
                    ApplyVisualProjectionChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                case OpenBIEntity.Table:
                    ApplyTableChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                case OpenBIEntity.Column:
                    ApplyColumnChange(entries, dirty, prefix, change, errors, warnings);
                    break;
                default:
                    warnings.Add(MakePatchError(change,
                        $"Entity '{change.Entity}' is not supported in PBIR patch operations. Change skipped."));
                    break;
            }
        }

        // ── Patch: AssetInfo ──────────────────────────────────────────────────────

        private static void ApplyAssetInfoChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            if (change.Op != OpenBIChangeOp.Replace)
            {
                errors.Add(MakePatchError(change, "AssetInfo only supports Replace operations."));
                return;
            }

            var jo = GetOrLoad(entries, dirty, PlatformPath(prefix));
            if (jo == null)
            {
                warnings.Add(MakePatchError(change, $"Platform file not found at '{PlatformPath(prefix)}'. Change skipped."));
                return;
            }

            foreach (var part in change.Parts)
            {
                if (part.Property.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    jo["metadata"] ??= new JObject();
                    ((JObject)jo["metadata"]!)["displayName"] = PatchDeserializeString(part.ValueJson);
                }
            }
        }

        // ── Patch: Page ───────────────────────────────────────────────────────────

        private static void ApplyPageChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:     ApplyPageAdd(entries, dirty, prefix, change, errors, warnings);     break;
                case OpenBIChangeOp.Remove:  ApplyPageRemove(entries, dirty, prefix, change, errors, warnings);  break;
                case OpenBIChangeOp.Replace: ApplyPageReplace(entries, dirty, prefix, change, errors, warnings); break;
            }
        }

        private static void ApplyPageAdd(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            if (string.IsNullOrEmpty(change.ValueJson))
            {
                errors.Add(MakePatchError(change, "Page Add requires ValueJson."));
                return;
            }

            JObject src;
            try { src = JObject.Parse(change.ValueJson); }
            catch (Exception ex) { errors.Add(MakePatchError(change, $"Failed to parse Page ValueJson: {ex.Message}", ex)); return; }

            var count  = entries.Keys.Count(k =>
                k.StartsWith($"{prefix}definition/pages/", StringComparison.OrdinalIgnoreCase) &&
                k.EndsWith("/page.json", StringComparison.OrdinalIgnoreCase));
            var pageId = ToPbirObjectId(src["Id"]?.ToString(), $"page-{count + 1}");

            var pageJo = new JObject
            {
                ["$schema"]       = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.0.0/schema.json",
                ["name"]          = pageId,
                ["displayName"]   = src["Name"]?.ToString() ?? pageId,
                ["displayOption"] = "FitToPage",
                ["width"]         = src["Width"]?.Value<double>()  ?? 1280.0,
                ["height"]        = src["Height"]?.Value<double>() ?? 720.0
            };
            if (src["IsEnabled"]?.Value<bool>() == false)
                pageJo["visibility"] = "HiddenInViewMode";

            entries[PageJsonPath(prefix, pageId)] = Utf8NoBom.GetBytes(pageJo.ToString(Formatting.Indented));

            var pagesJson = GetOrLoad(entries, dirty, PagesJsonPath(prefix));
            if (pagesJson?["pageOrder"] is JArray pageOrder)
                pageOrder.Add(pageId);
        }

        private static void ApplyPageRemove(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var pageId = change.Id;
            if (string.IsNullOrEmpty(pageId))
            {
                errors.Add(MakePatchError(change, "Page Remove requires an Id."));
                return;
            }

            var pagePrefix   = $"{prefix}definition/pages/{pageId}/";
            var keysToRemove = entries.Keys
                .Where(k => k.StartsWith(pagePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (keysToRemove.Count == 0)
            {
                warnings.Add(MakePatchError(change, $"Page '{pageId}' not found. Remove skipped."));
                return;
            }

            foreach (var key in keysToRemove)
            {
                entries.Remove(key);
            }

            var pagesJson = GetOrLoad(entries, dirty, PagesJsonPath(prefix));
            if (pagesJson != null)
            {
                if (pagesJson["pageOrder"] is JArray pageOrder)
                    pageOrder.FirstOrDefault(t => string.Equals(t.ToString(), pageId, StringComparison.Ordinal))
                        ?.Remove();

                if (string.Equals(pagesJson["activePage"]?.ToString(), pageId, StringComparison.Ordinal))
                    pagesJson["activePage"] =
                        (pagesJson["pageOrder"] as JArray)?.FirstOrDefault()?.ToString() ?? string.Empty;
            }
        }

        private static void ApplyPageReplace(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var pageId = change.Id;
            if (string.IsNullOrEmpty(pageId))
            {
                errors.Add(MakePatchError(change, "Page Replace requires an Id."));
                return;
            }

            var jo = GetOrLoad(entries, dirty, PageJsonPath(prefix, pageId));
            if (jo == null)
            {
                warnings.Add(MakePatchError(change, $"Page '{pageId}' not found. Replace skipped."));
                return;
            }

            foreach (var part in change.Parts)
            {
                switch (part.Property.ToLowerInvariant())
                {
                    case "name":
                        jo["displayName"] = PatchDeserializeString(part.ValueJson);
                        break;
                    case "width":
                        var w = PatchDeserializeDouble(part.ValueJson);
                        if (w.HasValue) jo["width"] = w.Value;
                        break;
                    case "height":
                        var h = PatchDeserializeDouble(part.ValueJson);
                        if (h.HasValue) jo["height"] = h.Value;
                        break;
                    case "isenabled":
                        if (PatchDeserializeBool(part.ValueJson))
                            jo.Remove("visibility");
                        else
                            jo["visibility"] = "HiddenInViewMode";
                        break;
                    case "order":
                        var newIdx = PatchDeserializeInt(part.ValueJson);
                        if (newIdx.HasValue)
                        {
                            var pagesJson = GetOrLoad(entries, dirty, PagesJsonPath(prefix));
                            if (pagesJson?["pageOrder"] is JArray pageOrder)
                            {
                                var token = pageOrder.FirstOrDefault(t =>
                                    string.Equals(t.ToString(), pageId, StringComparison.Ordinal));
                                if (token != null)
                                {
                                    token.Remove();
                                    var clampedIdx = Math.Max(0, Math.Min(newIdx.Value, pageOrder.Count));
                                    pageOrder.Insert(clampedIdx, pageId);
                                }
                            }
                        }
                        break;
                    // description, embedPageUrlParameter, embedPageUrl, additionalMetadata → not stored in page.json
                }
            }
        }

        // ── Patch: Visual ─────────────────────────────────────────────────────────

        private static void ApplyVisualChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:     ApplyVisualAdd(entries, dirty, prefix, change, errors, warnings);     break;
                case OpenBIChangeOp.Remove:  ApplyVisualRemove(entries, dirty, prefix, change, errors, warnings);  break;
                case OpenBIChangeOp.Replace: ApplyVisualReplace(entries, dirty, prefix, change, errors, warnings); break;
            }
        }

        private static void ApplyVisualAdd(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var pageId = change.ParentId;
            if (string.IsNullOrEmpty(pageId))
            {
                errors.Add(MakePatchError(change, "Visual Add requires ParentId (pageId)."));
                return;
            }
            if (string.IsNullOrEmpty(change.ValueJson))
            {
                errors.Add(MakePatchError(change, "Visual Add requires ValueJson."));
                return;
            }

            if (!entries.ContainsKey(PageJsonPath(prefix, pageId)))
            {
                warnings.Add(MakePatchError(change, $"Page '{pageId}' not found. Visual Add skipped."));
                return;
            }

            JObject src;
            try { src = JObject.Parse(change.ValueJson); }
            catch (Exception ex) { errors.Add(MakePatchError(change, $"Failed to parse Visual ValueJson: {ex.Message}", ex)); return; }

            var count    = entries.Keys.Count(k =>
                k.StartsWith($"{prefix}definition/pages/{pageId}/visuals/", StringComparison.OrdinalIgnoreCase) &&
                k.EndsWith("/visual.json", StringComparison.OrdinalIgnoreCase));
            var visualId = ToPbirObjectId(src["Id"]?.ToString(), $"visual-{pageId}-{count + 1}");

            var visualJo = new JObject
            {
                ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.2.0/schema.json",
                ["name"]    = visualId,
                ["position"] = new JObject
                {
                    ["x"]      = src["X"]?.Value<double>()      ?? 0.0,
                    ["y"]      = src["Y"]?.Value<double>()      ?? 0.0,
                    ["z"]      = src["Z"]?.Value<double>()      ?? 0.0,
                    ["width"]  = src["Width"]?.Value<double>()  ?? 240.0,
                    ["height"] = src["Height"]?.Value<double>() ?? 160.0
                },
                ["visual"] = new JObject
                {
                    ["visualType"]             = src["Type"]?.ToString() ?? "visualContainer",
                    ["drillFilterOtherVisuals"] = true
                }
            };

            if (src["VisualProjections"] is JArray projectionsArray && projectionsArray.Count > 0)
            {
                var (extensionKeys, extensionSchemaName) = GetExtensionMeasureKeysFromReportExt(entries, dirty, prefix);
                var queryState = BuildQueryStateJObject(projectionsArray, extensionKeys, extensionSchemaName);
                if (queryState.Count > 0)
                    ((JObject)visualJo["visual"]!)["query"] = new JObject { ["queryState"] = queryState };
            }

            entries[VisualJsonPath(prefix, pageId, visualId)] = Utf8NoBom.GetBytes(visualJo.ToString(Formatting.Indented));
        }

        private static void ApplyVisualRemove(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var visualId = change.Id;
            if (string.IsNullOrEmpty(visualId))
            {
                errors.Add(MakePatchError(change, "Visual Remove requires an Id."));
                return;
            }

            var (_, entryPath) = FindVisual(entries, prefix, visualId);
            if (entryPath == null)
            {
                warnings.Add(MakePatchError(change, $"Visual '{visualId}' not found. Remove skipped."));
                return;
            }

            entries.Remove(entryPath);
        }

        private static void ApplyVisualReplace(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var visualId = change.Id;
            if (string.IsNullOrEmpty(visualId))
            {
                errors.Add(MakePatchError(change, "Visual Replace requires an Id."));
                return;
            }

            var (_, entryPath) = FindVisual(entries, prefix, visualId);
            if (entryPath == null)
            {
                warnings.Add(MakePatchError(change, $"Visual '{visualId}' not found. Replace skipped."));
                return;
            }

            var jo = GetOrLoad(entries, dirty, entryPath);
            if (jo == null)
            {
                errors.Add(MakePatchError(change, $"Failed to load visual.json for '{visualId}'."));
                return;
            }

            foreach (var part in change.Parts)
            {
                switch (part.Property.ToLowerInvariant())
                {
                    case "name":
                        jo["name"] = PatchDeserializeString(part.ValueJson);
                        break;
                    case "type":
                        EnsurePath(jo, "visual")["visualType"] = PatchDeserializeString(part.ValueJson);
                        break;
                    case "x":
                        var x = PatchDeserializeDouble(part.ValueJson);
                        if (x.HasValue) EnsurePath(jo, "position")["x"] = x.Value;
                        break;
                    case "y":
                        var y = PatchDeserializeDouble(part.ValueJson);
                        if (y.HasValue) EnsurePath(jo, "position")["y"] = y.Value;
                        break;
                    case "z":
                        var z = PatchDeserializeDouble(part.ValueJson);
                        if (z.HasValue) EnsurePath(jo, "position")["z"] = z.Value;
                        break;
                    case "width":
                        var vw = PatchDeserializeDouble(part.ValueJson);
                        if (vw.HasValue) EnsurePath(jo, "position")["width"] = vw.Value;
                        break;
                    case "height":
                        var vh = PatchDeserializeDouble(part.ValueJson);
                        if (vh.HasValue) EnsurePath(jo, "position")["height"] = vh.Value;
                        break;
                    case "additionalmetadata":
                        if (!string.IsNullOrEmpty(part.ValueJson))
                        {
                            try
                            {
                                var items = JArray.Parse(part.ValueJson);
                                foreach (var item in items.Children<JObject>())
                                {
                                    var itemName  = item["Name"]?.ToString()  ?? item["name"]?.ToString();
                                    var itemValue = item["Value"]?.ToString() ?? item["value"]?.ToString();
                                    if (string.IsNullOrEmpty(itemName) || string.IsNullOrEmpty(itemValue)) continue;
                                    if (string.Equals(itemName, MetadataObjectsKey, StringComparison.OrdinalIgnoreCase))
                                        EnsurePath(jo, "visual")[MetadataObjectsKey] = JToken.Parse(itemValue);
                                    else if (string.Equals(itemName, MetadataVisualContainerObjectsKey, StringComparison.OrdinalIgnoreCase))
                                        EnsurePath(jo, "visual")[MetadataVisualContainerObjectsKey] = JToken.Parse(itemValue);
                                }
                            }
                            catch { warnings.Add(MakePatchError(change, "Malformed JSON in AdditionalMetadata. Metadata update skipped.")); }
                        }
                        break;
                    // category, openBIVisualType, description → not stored separately in visual.json
                }
            }
        }

        // ── Patch: VisualProjection ───────────────────────────────────────────────

        private static void ApplyVisualProjectionChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:     ApplyVisualProjectionAdd(entries, dirty, prefix, change, errors, warnings);     break;
                case OpenBIChangeOp.Remove:  ApplyVisualProjectionRemove(entries, dirty, prefix, change, errors, warnings);  break;
                case OpenBIChangeOp.Replace: ApplyVisualProjectionReplace(entries, dirty, prefix, change, errors, warnings); break;
            }
        }

        private static void ApplyVisualProjectionAdd(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var visualId = change.ParentId;
            if (string.IsNullOrEmpty(visualId))
            {
                errors.Add(MakePatchError(change, "VisualProjection Add requires ParentId (visualId)."));
                return;
            }
            if (string.IsNullOrEmpty(change.ValueJson))
            {
                errors.Add(MakePatchError(change, "VisualProjection Add requires ValueJson."));
                return;
            }

            var (_, entryPath) = FindVisual(entries, prefix, visualId);
            if (entryPath == null)
            {
                warnings.Add(MakePatchError(change, $"Visual '{visualId}' not found. VisualProjection Add skipped."));
                return;
            }

            JObject src;
            try { src = JObject.Parse(change.ValueJson); }
            catch (Exception ex) { errors.Add(MakePatchError(change, $"Failed to parse VisualProjection ValueJson: {ex.Message}", ex)); return; }

            var projectionName = src["ProjectionName"]?.ToString();
            var queryRef       = src["IdColumnReference"]?.ToString();
            var isMeasure      = src["IsMeasure"]?.Value<bool>() ?? false;
            var isActive       = src["IsActive"]?.Value<bool>()  ?? false;

            if (string.IsNullOrEmpty(projectionName) || string.IsNullOrEmpty(queryRef))
            {
                errors.Add(MakePatchError(change, "VisualProjection Add: ProjectionName and IdColumnReference are required."));
                return;
            }

            var jo         = GetOrLoad(entries, dirty, entryPath)!;
            var queryState = EnsurePath(EnsurePath(EnsurePath(jo, "visual"), "query"), "queryState");
            var bucket     = EnsurePath(queryState, projectionName);
            var projList   = bucket["projections"] as JArray ?? new JArray();
            bucket["projections"] = projList;
            var (extensionKeys, extensionSchemaName) = GetExtensionMeasureKeysFromReportExt(entries, dirty, prefix);
            projList.Add(BuildProjectionJObject(queryRef, isMeasure, isActive, extensionKeys, extensionSchemaName));
        }

        private static void ApplyVisualProjectionRemove(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            if (!VisualProjectionKey.TryDecode(change.Id, out var visualId, out var projectionName, out var order))
            {
                errors.Add(MakePatchError(change, $"Invalid VisualProjection key '{change.Id}'."));
                return;
            }

            var (_, entryPath) = FindVisual(entries, prefix, visualId);
            if (entryPath == null)
            {
                warnings.Add(MakePatchError(change, $"Visual '{visualId}' not found. VisualProjection Remove skipped."));
                return;
            }

            var jo          = GetOrLoad(entries, dirty, entryPath);
            var projections = jo?["visual"]?["query"]?["queryState"]?[projectionName]?["projections"] as JArray;

            if (projections == null)
            {
                warnings.Add(MakePatchError(change,
                    $"Projection bucket '{projectionName}' not found in visual '{visualId}'. Remove skipped."));
                return;
            }

            var zeroIdx = order - 1;
            if (zeroIdx < 0 || zeroIdx >= projections.Count)
            {
                warnings.Add(MakePatchError(change,
                    $"Projection index {order} out of range in bucket '{projectionName}' (count={projections.Count}). Remove skipped."));
                return;
            }

            projections[zeroIdx].Remove();
        }

        private static void ApplyVisualProjectionReplace(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            if (!VisualProjectionKey.TryDecode(change.Id, out var visualId, out var projectionName, out var order))
            {
                errors.Add(MakePatchError(change, $"Invalid VisualProjection key '{change.Id}'."));
                return;
            }

            var (_, entryPath) = FindVisual(entries, prefix, visualId);
            if (entryPath == null)
            {
                warnings.Add(MakePatchError(change, $"Visual '{visualId}' not found. VisualProjection Replace skipped."));
                return;
            }

            var jo          = GetOrLoad(entries, dirty, entryPath);
            if (jo == null)
            {
                warnings.Add(MakePatchError(change, $"Failed to load visual.json for '{visualId}'. VisualProjection Replace skipped."));
                return;
            }

            var queryState  = jo["visual"]?["query"]?["queryState"] as JObject;
            var bucket      = queryState?[projectionName] as JObject;
            var projections = bucket?["projections"] as JArray;

            if (projections == null)
            {
                warnings.Add(MakePatchError(change,
                    $"Projection bucket '{projectionName}' not found in visual '{visualId}'. Replace skipped."));
                return;
            }

            var zeroIdx = order - 1;
            if (zeroIdx < 0 || zeroIdx >= projections.Count)
            {
                warnings.Add(MakePatchError(change,
                    $"Projection index {order} out of range in bucket '{projectionName}' (count={projections.Count}). Replace skipped."));
                return;
            }

            if (projections[zeroIdx] is not JObject projection)
            {
                warnings.Add(MakePatchError(change,
                    $"Projection at index {order} in bucket '{projectionName}' is not a valid object. Replace skipped."));
                return;
            }

            foreach (var part in change.Parts)
            {
                switch (part.Property.ToLowerInvariant())
                {
                    case "isactive":
                        if (PatchDeserializeBool(part.ValueJson))
                            projection["active"] = true;
                        else
                            projection.Remove("active");
                        break;

                    case "idcolumnreference":
                        var newRef = PatchDeserializeString(part.ValueJson);
                        if (!string.IsNullOrEmpty(newRef))
                        {
                            var isMeasure = (projection["field"] as JObject)?.ContainsKey("Measure") ?? false;
                            var active    = projection["active"]?.Value<bool>() ?? false;
                            var (extensionKeys, extensionSchemaName) = GetExtensionMeasureKeysFromReportExt(entries, dirty, prefix);
                            var rebuilt   = BuildProjectionJObject(newRef, isMeasure, active, extensionKeys, extensionSchemaName);
                            projection["field"]         = rebuilt["field"];
                            projection["queryRef"]       = newRef;
                            projection["nativeQueryRef"] = BuildNativeQueryRef(newRef);
                        }
                        break;

                    case "order":
                        var newOrder = PatchDeserializeInt(part.ValueJson);
                        if (newOrder.HasValue)
                        {
                            var item = (JToken)projection;
                            item.Remove();
                            var newZeroIdx = Math.Max(0, Math.Min(newOrder.Value - 1, projections.Count));
                            projections.Insert(newZeroIdx, item);
                        }
                        break;

                    case "openbiprojectionname":
                        var newBucketName = PatchDeserializeString(part.ValueJson);
                        if (!string.IsNullOrEmpty(newBucketName) && queryState != null)
                        {
                            var item = (JToken)projection;
                            item.Remove();
                            if (projections.Count == 0) queryState.Remove(projectionName);
                            var destBucket = queryState[newBucketName] as JObject ?? new JObject();
                            var destList   = destBucket["projections"] as JArray ?? new JArray();
                            destBucket["projections"] = destList;
                            destList.Add(item);
                            queryState[newBucketName] = destBucket;
                        }
                        break;

                    // isDimension, isMeasure, expression, additionalMetadata, type → skipped
                }
            }
        }

        // ── Patch: Table (ReportExtension) ────────────────────────────────────────

        private static void ApplyTableChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var extPath = ReportExtPath(prefix);
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:
                {
                    if (string.IsNullOrEmpty(change.ValueJson))
                    {
                        errors.Add(MakePatchError(change, "Table Add requires ValueJson."));
                        return;
                    }
                    JObject src;
                    try { src = JObject.Parse(change.ValueJson); }
                    catch (Exception ex) { errors.Add(MakePatchError(change, $"Failed to parse Table ValueJson: {ex.Message}", ex)); return; }
                    var name = src["Name"]?.ToString();
                    if (string.IsNullOrEmpty(name)) { errors.Add(MakePatchError(change, "Table Add: Name is required.")); return; }
                    var jo = GetOrLoadOrCreate(entries, dirty, extPath, CreateEmptyReportExtension);
                    EnsureArray(jo, "entities").Add(new JObject { ["name"] = name, ["measures"] = new JArray() });
                    break;
                }
                case OpenBIChangeOp.Remove:
                {
                    if (string.IsNullOrEmpty(change.Id)) { errors.Add(MakePatchError(change, "Table Remove requires an Id.")); return; }
                    var jo = GetOrLoad(entries, dirty, extPath);
                    if (jo == null)
                    {
                        warnings.Add(MakePatchError(change, "reportExtensions.json not found. Table Remove skipped."));
                        return;
                    }
                    (jo["entities"] as JArray)
                        ?.FirstOrDefault(e => string.Equals(e["name"]?.ToString(), change.Id, StringComparison.Ordinal))
                        ?.Remove();
                    break;
                }
                case OpenBIChangeOp.Replace:
                {
                    if (string.IsNullOrEmpty(change.Id)) { errors.Add(MakePatchError(change, "Table Replace requires an Id.")); return; }
                    var jo = GetOrLoad(entries, dirty, extPath);
                    if (jo == null) { warnings.Add(MakePatchError(change, "reportExtensions.json not found. Table Replace skipped.")); return; }
                    var entity = (jo["entities"] as JArray)
                        ?.FirstOrDefault(e => string.Equals(e["name"]?.ToString(), change.Id, StringComparison.Ordinal)) as JObject;
                    if (entity == null) { warnings.Add(MakePatchError(change, $"Table '{change.Id}' not found. Replace skipped.")); return; }
                    foreach (var part in change.Parts)
                        if (part.Property.Equals("name", StringComparison.OrdinalIgnoreCase))
                            entity["name"] = PatchDeserializeString(part.ValueJson);
                    break;
                }
            }
        }

        // ── Patch: Column (ReportExtension measure) ───────────────────────────────

        private static void ApplyColumnChange(
            Dictionary<string, byte[]> entries,
            Dictionary<string, JObject> dirty,
            string prefix,
            OpenBIChange change,
            List<OpenBIPatchError> errors,
            List<OpenBIPatchError> warnings)
        {
            var extPath = ReportExtPath(prefix);
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:
                {
                    if (string.IsNullOrEmpty(change.ParentId)) { errors.Add(MakePatchError(change, "Column Add requires ParentId (table name).")); return; }
                    if (string.IsNullOrEmpty(change.ValueJson)) { errors.Add(MakePatchError(change, "Column Add requires ValueJson.")); return; }
                    JObject src;
                    try { src = JObject.Parse(change.ValueJson); }
                    catch (Exception ex) { errors.Add(MakePatchError(change, $"Failed to parse Column ValueJson: {ex.Message}", ex)); return; }
                    var jo = GetOrLoad(entries, dirty, extPath);
                    if (jo == null) { warnings.Add(MakePatchError(change, "reportExtensions.json not found. Column Add skipped.")); return; }
                    var entity = (jo["entities"] as JArray)
                        ?.FirstOrDefault(e => string.Equals(e["name"]?.ToString(), change.ParentId, StringComparison.Ordinal)) as JObject;
                    if (entity == null) { warnings.Add(MakePatchError(change, $"Table '{change.ParentId}' not found. Column Add skipped.")); return; }
                    var measureJo  = new JObject { ["name"] = src["Name"]?.ToString() };
                    var exprCode   = (src["Expression"] as JObject)?["Code"]?.ToString() ?? src["Expression"]?.ToString();
                    if (!string.IsNullOrEmpty(exprCode)) measureJo["expression"] = exprCode;
                    var dataType   = MapOpenBiDataTypeToReportExtensionPrimitiveType(
                        (Column.ColumnDataType)(src["DataType"]?.Value<int>() ?? 0));
                    if (dataType != PrimitiveTypeName.None) measureJo["dataType"] = dataType.ToString();
                    var desc = src["Description"]?.ToString();
                    if (!string.IsNullOrEmpty(desc)) measureJo["description"] = desc;
                    EnsureArray(entity, "measures").Add(measureJo);
                    break;
                }
                case OpenBIChangeOp.Remove:
                {
                    if (string.IsNullOrEmpty(change.Id)) { errors.Add(MakePatchError(change, "Column Remove requires an Id.")); return; }
                    var jo = GetOrLoad(entries, dirty, extPath);
                    if (jo == null)
                    {
                        warnings.Add(MakePatchError(change, "reportExtensions.json not found. Column Remove skipped."));
                        return;
                    }
                    foreach (var entity in (jo["entities"] as JArray ?? new JArray()).Children<JObject>())
                    {
                        var m = (entity["measures"] as JArray)
                            ?.FirstOrDefault(x => string.Equals(x["name"]?.ToString(), change.Id, StringComparison.Ordinal));
                        if (m != null) { m.Remove(); return; }
                    }
                    break;
                }
                case OpenBIChangeOp.Replace:
                {
                    if (string.IsNullOrEmpty(change.Id)) { errors.Add(MakePatchError(change, "Column Replace requires an Id.")); return; }
                    var jo = GetOrLoad(entries, dirty, extPath);
                    if (jo == null) { warnings.Add(MakePatchError(change, "reportExtensions.json not found. Column Replace skipped.")); return; }
                    JObject? measure = null;
                    foreach (var entity in (jo["entities"] as JArray ?? new JArray()).Children<JObject>())
                    {
                        measure = (entity["measures"] as JArray)
                            ?.FirstOrDefault(x => string.Equals(x["name"]?.ToString(), change.Id, StringComparison.Ordinal)) as JObject;
                        if (measure != null) break;
                    }
                    if (measure == null) { warnings.Add(MakePatchError(change, $"Column '{change.Id}' not found. Replace skipped.")); return; }
                    foreach (var part in change.Parts)
                    {
                        switch (part.Property.ToLowerInvariant())
                        {
                            case "name":
                                measure["name"] = PatchDeserializeString(part.ValueJson);
                                break;
                            case "expression":
                                var exprSrc = string.IsNullOrEmpty(part.ValueJson) ? null : JObject.Parse(part.ValueJson);
                                var code    = exprSrc?["Code"]?.ToString();
                                if (code != null) measure["expression"] = code;
                                break;
                            case "datatype":
                                var dtInt  = part.ValueJson != null ? JToken.Parse(part.ValueJson).Value<int>() : 0;
                                var ptype  = MapOpenBiDataTypeToReportExtensionPrimitiveType((Column.ColumnDataType)dtInt);
                                if (ptype != PrimitiveTypeName.None) measure["dataType"] = ptype.ToString();
                                break;
                            case "description":
                                measure["description"] = PatchDeserializeString(part.ValueJson);
                                break;
                        }
                    }
                    break;
                }
            }
        }

        // ── Patch: projection helpers ─────────────────────────────────────────────

        private static JObject BuildProjectionJObject(
            string queryRef,
            bool isMeasure,
            bool isActive,
            HashSet<string> extensionKeys,
            string extensionSchemaName)
        {
            var (entity, property) = ParseEntityProperty(queryRef);
            var fieldKey = isMeasure ? "Measure" : "Column";
            var measureSchema = IsExtensionMeasure(queryRef, isMeasure, extensionKeys)
                ? extensionSchemaName
                : null;

            var innerField = string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(property)
                ? new JObject
                {
                    ["Expression"] = new JObject { ["SourceRef"] = new JObject { ["Entity"] = "Unknown" } },
                    ["Property"]   = queryRef
                }
                : new JObject
                {
                    ["Expression"] = new JObject { ["SourceRef"] = BuildSourceRefJObject(entity, measureSchema) },
                    ["Property"]   = property
                };

            var proj = new JObject
            {
                ["field"]          = new JObject { [fieldKey] = innerField },
                ["queryRef"]       = queryRef,
                ["nativeQueryRef"] = BuildNativeQueryRef(queryRef)
            };
            if (isActive) proj["active"] = true;
            return proj;
        }

        private static JObject BuildQueryStateJObject(
            JArray projectionsArray,
            HashSet<string> extensionKeys,
            string extensionSchemaName)
        {
            var queryState = new JObject();
            var grouped = projectionsArray.Children<JObject>()
                .Where(p => !string.IsNullOrEmpty(p["ProjectionName"]?.ToString()))
                .GroupBy(p => p["ProjectionName"]!.ToString());

            foreach (var group in grouped)
            {
                var projList = new JArray();
                foreach (var src in group.OrderBy(p => p["Order"]?.Value<int>() ?? 0))
                {
                    var qRef      = src["IdColumnReference"]?.ToString();
                    if (string.IsNullOrEmpty(qRef)) continue;
                    var isMeasure = src["IsMeasure"]?.Value<bool>() ?? false;
                    var isActive  = src["IsActive"]?.Value<bool>()  ?? false;
                    projList.Add(BuildProjectionJObject(qRef, isMeasure, isActive, extensionKeys, extensionSchemaName));
                }
                if (projList.Count > 0)
                    queryState[group.Key] = new JObject { ["projections"] = projList };
            }
            return queryState;
        }

        // ── Patch: JObject utilities ──────────────────────────────────────────────

        private static JObject EnsurePath(JObject parent, string key)
        {
            if (parent[key] is JObject existing) return existing;
            var child = new JObject();
            parent[key] = child;
            return child;
        }

        private static JArray EnsureArray(JObject parent, string key)
        {
            if (parent[key] is JArray existing) return existing;
            var arr = new JArray();
            parent[key] = arr;
            return arr;
        }

        private static JObject CreateEmptyReportExtension() => new JObject
        {
            ["$schema"]  = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/reportExtension/1.0.0/schema.json",
            ["name"]     = "extension",
            ["entities"] = new JArray()
        };

        // ── Patch: error / deserialize helpers ────────────────────────────────────

        private static OpenBIPatchError MakePatchError(OpenBIChange change, string message, Exception? ex = null) =>
            new OpenBIPatchError
            {
                Entity         = change.Entity,
                Id             = change.Id,
                Op             = change.Op,
                Message        = message,
                InnerException = ex
            };

        private static OpenBIPatchError MakePatchPartError(OpenBIChange change, OpenBIChangePart part,
            string message, Exception? ex = null) =>
            new OpenBIPatchError
            {
                Entity         = change.Entity,
                Id             = change.Id,
                Property       = part.Property,
                Op             = change.Op,
                Message        = message,
                InnerException = ex
            };

        private static string? PatchDeserializeString(string? valueJson)
        {
            if (valueJson is null) return null;
            return System.Text.Json.JsonSerializer.Deserialize<string>(valueJson);
        }

        private static bool PatchDeserializeBool(string? valueJson)
        {
            if (valueJson is null) return false;
            return System.Text.Json.JsonSerializer.Deserialize<bool>(valueJson);
        }

        private static double? PatchDeserializeDouble(string? valueJson)
        {
            if (string.IsNullOrEmpty(valueJson)) return null;
            try { return JToken.Parse(valueJson).Value<double>(); }
            catch { return null; }
        }

        private static int? PatchDeserializeInt(string? valueJson)
        {
            if (string.IsNullOrEmpty(valueJson)) return null;
            try { return JToken.Parse(valueJson).Value<int>(); }
            catch { return null; }
        }

        /// <summary>
        /// Legacy report.json ingestion hook. Throws by default; platform extensions may override.
        /// </summary>
        protected virtual Task<Asset> ReadLegacyReportAsync(byte[] artifact)
            => throw new NotSupportedException("Legacy report.json unsupported.");
    }
}
