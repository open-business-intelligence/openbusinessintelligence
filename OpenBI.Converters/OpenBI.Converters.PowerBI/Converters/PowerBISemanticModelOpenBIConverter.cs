using OpenBI.Interfaces.Infrastructure;
using OpenBI.Converters.Interfaces;
using OpenBI.Converters.PowerBI.Models.Schema;
using Microsoft.AnalysisServices;
using Microsoft.Extensions.Logging;
using Tom = Microsoft.AnalysisServices.Tabular;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBI;
using OpenBI.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static OpenBI.Column;
using AdminDataset = Microsoft.PowerBI.Api.Models.AdminDataset;
using PowerBIDatasource = Microsoft.PowerBI.Api.Models.Datasource;
using PowerBIDatasourceConnectionDetails = Microsoft.PowerBI.Api.Models.DatasourceConnectionDetails;

namespace OpenBI.Converters.PowerBI.Converters
{
    public class PowerBISemanticModelOpenBIConverter : IOpenBIConverter
    {
        private const string DefaultSemanticModelTemplateResourceName = "OpenBI.Converters.PowerBI.Resources.Templates.DefaultSemanticModelLayout.json";
        private const string SemanticModelFolderSuffix = ".SemanticModel";
        private const string ModelBimFileName = "model.bim";
        private const string DefinitionFolderSegment = "definition/";
        private const string InfoJsonFileName = "info.json";
        private const string ConnectionsJsonFileName = "connections.json";
        private const string PlatformFileName = ".platform";
        private const string DefinitionPbismFileName = "definition.pbism";
        private const string PlatformSchema = "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json";
        private const string DefaultAssetName = "SemanticModel";

        private IArtifactCompressionService? _compression;
        private ILogger? _logger;

        public PowerBISemanticModelOpenBIConverter(ILogger<PowerBISemanticModelOpenBIConverter> logger, IArtifactCompressionService compressionService)
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

        private void EnsureCompressionService()
        {
            if (_compression == null)
                throw new InvalidOperationException("Compression service has not been set. Call SetCompressionService before using conversion methods that require compression.");
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

            _logger?.LogInformation("Converting PowerBI semantic model artifact to OpenBI asset (size={Size} bytes)", artifact.Length);
            var dataSourceConnections = TryReadDataSourceConnectionsFromZip(artifact);

            var (bimFound, modelBimBytes) = TryReadModelBimFromZip(artifact);
            if (bimFound && modelBimBytes != null)
            {
                var bimJson = Encoding.UTF8.GetString(modelBimBytes);
                var root = JsonConvert.DeserializeObject<Root>(bimJson);
                if (root == null)
                    throw new InvalidOperationException("Failed to deserialize model.bim.");

                var (tables, schemaRelationships) = ParseBimToOpenBI(root);
                var assetInfo = ReadAssetInfoFromZip(artifact, root, tables);
                var asset = new Asset
                {
                    Info = assetInfo,
                    Layout = new Layout { Pages = new List<Page>() },
                    DataModel = new DataModel { Tables = tables, Relationships = (schemaRelationships ?? new List<Relationship>()).ToList() },
                    DataSourceConnections = dataSourceConnections,
                    Dependencies = Array.Empty<Asset>()
                };
                _logger?.LogInformation("Converted PowerBI semantic model artifact to OpenBI asset: {AssetName}, tables={TablesCount}", asset.Info?.Name, asset.DataModel?.Tables?.Count ?? 0);
                return await Task.FromResult(asset).ConfigureAwait(false);
            }

            var (defFound, tempDirPath) = TryGetDefinitionFolderPathFromZip(artifact);
            if (!defFound || string.IsNullOrEmpty(tempDirPath))
                throw new InvalidOperationException("Artifact contains neither model.bim nor a definition folder.");

            try
            {
                var (tables, schemaRelationships, tmdlModelName) = ParseTmdlToOpenBI(tempDirPath);
                var assetInfo = ReadAssetInfoFromZip(artifact, null, tables, tmdlModelName);
                var asset = new Asset
                {
                    Info = assetInfo,
                    Layout = new Layout { Pages = new List<Page>() },
                    DataModel = new DataModel { Tables = tables, Relationships = (schemaRelationships ?? new List<Relationship>()).ToList() },
                    DataSourceConnections = dataSourceConnections,
                    Dependencies = Array.Empty<Asset>()
                };

                _logger?.LogInformation("Converted PowerBI semantic model artifact (TMDL) to OpenBI asset: {AssetName}, tables={TablesCount}", asset.Info?.Name, asset.DataModel?.Tables?.Count ?? 0);
                return await Task.FromResult(asset).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(tempDirPath) && Directory.Exists(tempDirPath))
                        Directory.Delete(tempDirPath, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete temporary TMDL extraction folder: {Path}", tempDirPath);
                }
            }
        }

        public async Task<byte[]> FromOpenBIToArtifactAsync(Asset asset)
        {
            EnsureCompressionService();
            if (asset?.Info == null)
                throw new ArgumentNullException(nameof(asset));

            _logger?.LogInformation("Converting OpenBI asset to PowerBI semantic model artifact (TMDL): {AssetName}", asset.Info.Name);

            var safeName = string.IsNullOrWhiteSpace(asset.Info.Name) ? DefaultAssetName : asset.Info.Name;
            var tempDir = Path.Combine(Path.GetTempPath(), "openbi_tmdl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var db = BuildTabularDatabaseFromAsset(asset, safeName);
                TmdlSerializer.SerializeDatabaseToFolder(db, tempDir);
                var zipBytes = BuildSemanticModelZipFromDefinitionFolder(safeName, tempDir, asset.Info);
                _logger?.LogInformation("Converted OpenBI asset to PowerBI semantic model artifact: {AssetName}, size={Size} bytes", asset.Info.Name, zipBytes.Length);
                return zipBytes;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete temporary TMDL folder: {Path}", tempDir);
                }
            }
        }

        /// <summary>
        /// Legacy export path: OpenBI → TMSL JSON (<c>model.bim</c>) inside the semantic model zip.
        /// </summary>
        public async Task<byte[]> FromOpenBIToArtifactAsync_Old(Asset asset)
        {
            EnsureCompressionService();
            if (asset?.Info == null)
                throw new ArgumentNullException(nameof(asset));

            _logger?.LogInformation("Converting OpenBI asset to PowerBI semantic model artifact (BIM): {AssetName}", asset.Info.Name);

            var modelBimBytes = BuildModelBimBytes(asset, existingModelBimBytes: null);
            var safeName = string.IsNullOrWhiteSpace(asset.Info.Name) ? DefaultAssetName : asset.Info.Name;
            var zipBytes = BuildSemanticModelZip(safeName, modelBimBytes, asset.Info);
            var result = await _compression!.ZipAsync(zipBytes).ConfigureAwait(false);

            _logger?.LogInformation("Converted OpenBI asset to PowerBI semantic model artifact: {AssetName}, size={Size} bytes", asset.Info.Name, result.Length);
            return result;
        }

        public async Task<OpenBIPatchResult> FromOpenBIPatchArtifactAsync(IEnumerable<OpenBIChange> changes, byte[] artifact)
        {
            EnsureCompressionService();
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (artifact == null || artifact.Length == 0) throw new ArgumentNullException(nameof(artifact));

            _logger?.LogInformation("Patching PowerBI semantic model via change list, artifact size={Size}", artifact.Length);

            var (isTmdl, tempDir) = TryGetDefinitionFolderPathFromZip(artifact);
            if (!isTmdl || string.IsNullOrEmpty(tempDir))
                throw new NotSupportedException(
                    "Change-list patch is only supported for TMDL (definition/ folder) semantic model artifacts. " +
                    "BIM (model.bim) artifacts are not supported for incremental patching.");

            var errors   = new List<OpenBIPatchError>();
            var warnings = new List<OpenBIPatchError>();
            try
            {
                var db = Microsoft.AnalysisServices.TmdlSerializer.DeserializeDatabaseFromFolder(tempDir);
                if (db?.Model == null)
                    throw new InvalidOperationException("Failed to deserialize TMDL model — database or model is null.");

                foreach (var change in changes)
                {
                    try { ApplyTomChange(db.Model, change, errors, warnings); }
                    catch (Exception ex)
                    {
                        errors.Add(new OpenBIPatchError
                        {
                            Entity = change.Entity, Id = change.Id, Op = change.Op,
                            Message = $"Unexpected error applying change: {ex.Message}",
                            InnerException = ex
                        });
                    }
                }

                var outTempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(outTempDir);
                try
                {
                    Microsoft.AnalysisServices.TmdlSerializer.SerializeDatabaseToFolder(db, outTempDir);
                    var assetInfo = ReadAssetInfoFromZip(artifact, null, new List<OpenBI.Table>(), db.Model.Name);
                    var resultZip = BuildSemanticModelZipFromDefinitionFolder(
                        assetInfo.Name ?? DefaultAssetName, outTempDir, assetInfo);

                    return new OpenBIPatchResult { Artifact = resultZip, Errors = errors, Warnings = warnings };
                }
                finally { try { Directory.Delete(outTempDir, recursive: true); } catch { } }
            }
            finally { try { Directory.Delete(tempDir, recursive: true); } catch { } }
        }

        private static void ApplyTomChange(
            Tom.Model model, OpenBIChange change,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings)
        {
            switch (change.Entity)
            {
                case OpenBIEntity.Table:        ApplyTomTableChange(model, change, errors, warnings);        break;
                case OpenBIEntity.Column:       ApplyTomColumnChange(model, change, errors, warnings);       break;
                case OpenBIEntity.Relationship: ApplyTomRelationshipChange(model, change, errors, warnings); break;
                default:
                    // Unknown entity for this converter — not blocking, other changes can still apply
                    warnings.Add(new OpenBIPatchError
                    {
                        Entity = change.Entity, Id = change.Id, Op = change.Op,
                        Message = $"Entity '{change.Entity}' is not supported for semantic model patch. Change skipped."
                    });
                    break;
            }
        }

        private static void ApplyTomTableChange(
            Tom.Model model, OpenBIChange change,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:
                {
                    if (string.IsNullOrEmpty(change.ValueJson))
                    { errors.Add(MakePatchError(change, "ValueJson is required for Add.")); return; }
                    var table = DeserializeChangePart<OpenBI.Table>(change.ValueJson);
                    if (table == null)
                    { errors.Add(MakePatchError(change, "Failed to deserialize Table from ValueJson.")); return; }
                    model.Tables.Add(BuildTomTable(table));
                    break;
                }
                case OpenBIChangeOp.Remove:
                {
                    var existing = FindTomTable(model, change.Id);
                    if (existing == null)
                    { warnings.Add(MakePatchError(change, $"Table '{change.Id}' not found. Remove skipped.")); return; }
                    model.Tables.Remove(existing);
                    break;
                }
                case OpenBIChangeOp.Replace:
                {
                    var existing = FindTomTable(model, change.Id);
                    if (existing == null)
                    { warnings.Add(MakePatchError(change, $"Table '{change.Id}' not found. Replace skipped.")); return; }
                    foreach (var part in change.Parts)
                    {
                        switch (part.Property.ToLowerInvariant())
                        {
                            case "name":
                                existing.Name = DeserializeChangePart<string>(part.ValueJson) ?? existing.Name;
                                break;
                            case "expression":
                            {
                                var expr = part.ValueJson != null ? DeserializeChangePart<Expression>(part.ValueJson) : null;
                                var code = expr?.Code ?? string.Empty;
                                var partition = existing.Partitions.FirstOrDefault();
                                if (partition?.Source is Tom.MPartitionSource mSrc)
                                    mSrc.Expression = code;
                                else if (partition?.Source is Tom.CalculatedPartitionSource calcSrc)
                                    calcSrc.Expression = code;
                                break;
                            }
                            default:
                                warnings.Add(new OpenBIPatchError
                                {
                                    Entity = change.Entity, Id = change.Id,
                                    Property = part.Property, Op = change.Op,
                                    Message = $"Property '{part.Property}' is not supported for Table Replace. Property skipped."
                                });
                                break;
                        }
                    }
                    break;
                }
            }
        }

        private static void ApplyTomColumnChange(
            Tom.Model model, OpenBIChange change,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:
                {
                    if (string.IsNullOrEmpty(change.ValueJson))
                    { errors.Add(MakePatchError(change, "ValueJson is required for Add.")); return; }
                    var col = DeserializeChangePart<OpenBI.Column>(change.ValueJson);
                    if (col == null)
                    { errors.Add(MakePatchError(change, "Failed to deserialize Column from ValueJson.")); return; }
                    var parentTable = FindTomTable(model, change.ParentId);
                    if (parentTable == null)
                    { warnings.Add(MakePatchError(change, $"Parent table '{change.ParentId}' not found. Column Add skipped.")); return; }
                    AddTomColumnToTable(parentTable, col);
                    break;
                }
                case OpenBIChangeOp.Remove:
                {
                    var (table, col, mea) = FindTomColumnOrMeasure(model, change.Id);
                    if (col == null && mea == null)
                    { warnings.Add(MakePatchError(change, $"Column/Measure '{change.Id}' not found. Remove skipped.")); return; }
                    if (mea != null) table!.Measures.Remove(mea);
                    else table!.Columns.Remove(col!);
                    break;
                }
                case OpenBIChangeOp.Replace:
                {
                    var (_, col, mea) = FindTomColumnOrMeasure(model, change.Id);
                    if (col == null && mea == null)
                    { warnings.Add(MakePatchError(change, $"Column/Measure '{change.Id}' not found. Replace skipped.")); return; }
                    foreach (var part in change.Parts)
                        ApplyTomColumnPart(col, mea, part, errors, warnings, change);
                    break;
                }
            }
        }

        private static void ApplyTomColumnPart(
            Tom.Column? col, Tom.Measure? mea, OpenBIChangePart part,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings, OpenBIChange change)
        {
            if (mea != null)
            {
                switch (part.Property.ToLowerInvariant())
                {
                    case "name":
                        mea.Name = DeserializeChangePart<string>(part.ValueJson) ?? mea.Name;
                        break;
                    case "expression":
                    {
                        var expr = part.ValueJson != null ? DeserializeChangePart<Expression>(part.ValueJson) : null;
                        mea.Expression = expr?.Code ?? mea.Expression;
                        break;
                    }
                    case "formatstring":
                        mea.FormatString = DeserializeChangePart<string>(part.ValueJson) ?? mea.FormatString;
                        break;
                    default:
                        warnings.Add(new OpenBIPatchError
                        {
                            Entity = change.Entity, Id = change.Id,
                            Property = part.Property, Op = change.Op,
                            Message = $"Property '{part.Property}' is not supported for Measure Replace. Property skipped."
                        });
                        break;
                }
                return;
            }

            switch (part.Property.ToLowerInvariant())
            {
                case "name":
                    col!.Name = DeserializeChangePart<string>(part.ValueJson) ?? col.Name;
                    break;
                case "datatype":
                {
                    var dt = DeserializeChangePart<ColumnDataType>(part.ValueJson);
                    col!.DataType = MapOpenBiDataTypeToTabular(dt);
                    break;
                }
                case "expression":
                {
                    if (col is Tom.CalculatedColumn calcCol)
                    {
                        var expr = part.ValueJson != null ? DeserializeChangePart<Expression>(part.ValueJson) : null;
                        calcCol.Expression = expr?.Code ?? calcCol.Expression;
                    }
                    break;
                }
                case "formatstring":
                    col!.FormatString = DeserializeChangePart<string>(part.ValueJson);
                    break;
                case "datacategory":
                    col!.DataCategory = DeserializeChangePart<string>(part.ValueJson);
                    break;
                case "iskey":
                    col!.IsKey = DeserializeChangePart<bool>(part.ValueJson);
                    break;
                case "isnullable":
                    col!.IsNullable = DeserializeChangePart<bool>(part.ValueJson);
                    break;
                case "isunique":
                    col!.IsUnique = DeserializeChangePart<bool>(part.ValueJson);
                    break;
                case "summarizeby":
                    ApplySummarizeByIfPresent(col!, DeserializeChangePart<string>(part.ValueJson));
                    break;
                default:
                    warnings.Add(new OpenBIPatchError
                    {
                        Entity = change.Entity, Id = change.Id,
                        Property = part.Property, Op = change.Op,
                        Message = $"Property '{part.Property}' is not supported for Column Replace. Property skipped."
                    });
                    break;
            }
        }

        private static void ApplyTomRelationshipChange(
            Tom.Model model, OpenBIChange change,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings)
        {
            switch (change.Op)
            {
                case OpenBIChangeOp.Add:
                {
                    if (string.IsNullOrEmpty(change.ValueJson))
                    { errors.Add(MakePatchError(change, "ValueJson is required for Add.")); return; }
                    var rel = DeserializeChangePart<Relationship>(change.ValueJson);
                    if (rel == null)
                    { errors.Add(MakePatchError(change, "Failed to deserialize Relationship from ValueJson.")); return; }
                    AddTomRelationshipToModel(model, rel, errors, warnings, change);
                    break;
                }
                case OpenBIChangeOp.Remove:
                {
                    var existing = model.Relationships.FirstOrDefault(r => r.Name == change.Id);
                    if (existing == null)
                    { warnings.Add(MakePatchError(change, $"Relationship '{change.Id}' not found. Remove skipped.")); return; }
                    model.Relationships.Remove(existing);
                    break;
                }
                case OpenBIChangeOp.Replace:
                {
                    // TOM column refs can't be mutated in-place — Remove + Add
                    var existing = model.Relationships.FirstOrDefault(r => r.Name == change.Id);
                    if (existing == null)
                    { warnings.Add(MakePatchError(change, $"Relationship '{change.Id}' not found. Replace skipped.")); return; }
                    if (string.IsNullOrEmpty(change.ValueJson))
                    { errors.Add(MakePatchError(change, "ValueJson with full Relationship is required for Replace.")); return; }
                    var newRel = DeserializeChangePart<Relationship>(change.ValueJson);
                    if (newRel == null)
                    { errors.Add(MakePatchError(change, "Failed to deserialize replacement Relationship.")); return; }
                    model.Relationships.Remove(existing);
                    AddTomRelationshipToModel(model, newRel, errors, warnings, change);
                    break;
                }
            }
        }

        private static void AddTomRelationshipToModel(
            Tom.Model model, Relationship rel,
            List<OpenBIPatchError> errors, List<OpenBIPatchError> warnings, OpenBIChange change)
        {
            var columnIdToColumn = new Dictionary<string, Tom.Column>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in model.Tables)
                foreach (var c in t.Columns)
                    if (!string.IsNullOrEmpty(c.LineageTag))
                        columnIdToColumn[c.LineageTag] = c;

            if (!TryResolveRelationshipToTomEndpoints(
                    rel, columnIdToColumn,
                    out var fromCol, out var toCol, out var fromCard, out var toCard, out var relName))
            {
                warnings.Add(MakePatchError(change,
                    $"Relationship columns not found: fromId='{rel.IdColumnFrom}', toId='{rel.IdColumnTo}'. Relationship skipped."));
                return;
            }

            model.Relationships.Add(new Tom.SingleColumnRelationship
            {
                Name = relName,
                FromColumn = fromCol,
                ToColumn = toCol,
                FromCardinality = StringCardinalityToRelationshipEnd(fromCard),
                ToCardinality = StringCardinalityToRelationshipEnd(toCard)
            });
        }

        private static Tom.Table BuildTomTable(OpenBI.Table table)
        {
            var tomTable = new Tom.Table
            {
                Name = table.Name ?? "Table",
                LineageTag = NormalizeLineageTag(table.Id)
            };
            foreach (var col in table.Columns ?? Array.Empty<OpenBI.Column>())
                AddTomColumnToTable(tomTable, col);
            var mExpr = string.IsNullOrEmpty(GetCode(table.Expression)) ? "\"\"" : GetCode(table.Expression)!;
            tomTable.Partitions.Add(new Tom.Partition
            {
                Name = "Partition",
                Mode = Tom.ModeType.Import,
                Source = new Tom.MPartitionSource { Expression = mExpr }
            });
            return tomTable;
        }

        private static void AddTomColumnToTable(Tom.Table tomTable, OpenBI.Column col)
        {
            var colId = col.Id ?? Guid.NewGuid().ToString();
            if (string.Equals(col.Type, "measure", StringComparison.OrdinalIgnoreCase))
            {
                tomTable.Measures.Add(new Tom.Measure
                {
                    Name = col.Name ?? "Measure",
                    LineageTag = NormalizeLineageTag(colId),
                    Expression = string.IsNullOrEmpty(GetCode(col.Expression)) ? "0" : GetCode(col.Expression)!
                });
                return;
            }
            if (string.Equals(col.Type, "calculated", StringComparison.OrdinalIgnoreCase))
            {
                var calc = new Tom.CalculatedColumn
                {
                    Name = col.Name ?? "Column",
                    LineageTag = NormalizeLineageTag(colId),
                    DataType = MapOpenBiDataTypeToTabular(col.DataType),
                    IsKey = col.IsKey, IsNullable = col.IsNullable, IsUnique = col.IsUnique,
                    FormatString = col.FormatString, DataCategory = col.DataCategory,
                    Expression = GetCode(col.Expression) ?? string.Empty
                };
                ApplySummarizeByIfPresent(calc, col.SummarizeBy);
                tomTable.Columns.Add(calc);
                return;
            }
            var dataCol = new Tom.DataColumn
            {
                Name = col.Name ?? "Column",
                LineageTag = NormalizeLineageTag(colId),
                DataType = MapOpenBiDataTypeToTabular(col.DataType),
                IsKey = col.IsKey, IsNullable = col.IsNullable, IsUnique = col.IsUnique,
                FormatString = col.FormatString, DataCategory = col.DataCategory
            };
            ApplySummarizeByIfPresent(dataCol, col.SummarizeBy);
            tomTable.Columns.Add(dataCol);
        }

        private static Tom.Table? FindTomTable(Tom.Model model, string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return model.Tables.FirstOrDefault(t => t.LineageTag == id)
                ?? model.Tables.FirstOrDefault(t =>
                       string.Equals(t.Name, id, StringComparison.OrdinalIgnoreCase));
        }

        private static (Tom.Table? table, Tom.Column? col, Tom.Measure? mea) FindTomColumnOrMeasure(
            Tom.Model model, string? id)
        {
            if (string.IsNullOrEmpty(id)) return (null, null, null);
            foreach (var t in model.Tables)
            {
                var col = t.Columns.FirstOrDefault(c => c.LineageTag == id);
                if (col != null) return (t, col, null);
                var mea = t.Measures.FirstOrDefault(m => m.LineageTag == id);
                if (mea != null) return (t, null, mea);
            }
            return (null, null, null);
        }

        private static OpenBIPatchError MakePatchError(OpenBIChange change, string message) =>
            new() { Entity = change.Entity, Id = change.Id, Op = change.Op, Message = message };

        private static T? DeserializeChangePart<T>(string? json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(
                    json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return default; }
        }

        private static (bool found, byte[]? modelBimBytes) TryReadModelBimFromZip(byte[] zipBytes)
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(ModelBimFileName, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(ModelBimFileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return (false, null);
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            return (true, ms.ToArray());
        }

        private static byte[] ReadModelBimFromZip(byte[] zipBytes)
        {
            var (found, modelBimBytes) = TryReadModelBimFromZip(zipBytes);
            if (!found || modelBimBytes == null)
                throw new FileNotFoundException("No model.bim entry found in the provided ZIP.");
            return modelBimBytes;
        }

        private static (bool found, string? tempDirPath) TryGetDefinitionFolderPathFromZip(byte[] zipBytes)
        {
            const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
            string? definitionPrefix = null;
            using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false))
            {
                var definitionEntry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.IndexOf(DefinitionFolderSegment, cmp) >= 0 && !string.IsNullOrEmpty(e.Name));
                if (definitionEntry == null)
                    return (false, null);
                var idx = definitionEntry.FullName.IndexOf(DefinitionFolderSegment, cmp);
                definitionPrefix = definitionEntry.FullName.Substring(0, idx + DefinitionFolderSegment.Length);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(definitionPrefix, cmp)))
                {
                    var relativeName = entry.FullName.Substring(definitionPrefix.Length);
                    if (string.IsNullOrEmpty(relativeName))
                        continue;
                    var destPath = Path.Combine(tempDir, relativeName.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.Name == "")
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
                return (true, tempDir);
            }
            catch
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                throw;
            }
        }

        private static AssetInfo ReadAssetInfoFromZip(byte[] zipBytes, Root? bimRoot, List<OpenBI.Table> tables, string? modelName = null)
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
            var platformDisplayName = TryReadSemanticModelPlatformDisplayName(zip);
            var infoEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(InfoJsonFileName, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(InfoJsonFileName, StringComparison.OrdinalIgnoreCase));
            if (infoEntry != null)
            {
                using var es = infoEntry.Open();
                using var reader = new StreamReader(es, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var json = reader.ReadToEnd();
                var info = JsonConvert.DeserializeObject<AdminDataset>(json);
                if (info != null)
                {
                    var nameFromInfo = info.Name ?? DefaultAssetName;
                    var resolvedName = !string.IsNullOrEmpty(platformDisplayName) ? platformDisplayName : nameFromInfo;
                    return new AssetInfo
                    {
                        Id = info.Id.ToString(),
                        Name = resolvedName,
                        Description = info.Description ?? string.Empty,
                        Type = AssetType.DataModel,
                        ExternalType = "SemanticModel"
                    };
                }
            }
            var fallbackName = modelName ?? bimRoot?.name;
            if (string.IsNullOrWhiteSpace(fallbackName) && tables != null && tables.Count > 0)
                fallbackName = tables[0].Name;
            var nameFromFallback = fallbackName ?? DefaultAssetName;
            var resolvedFallbackName = !string.IsNullOrEmpty(platformDisplayName) ? platformDisplayName : nameFromFallback;
            return new AssetInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = resolvedFallbackName,
                Description = string.Empty,
                Type = AssetType.DataModel,
                ExternalType = "SemanticModel"
            };
        }

        /// <summary>
        /// Reads <c>metadata.displayName</c> from a Fabric <c>*.SemanticModel/.platform</c> entry when present.
        /// </summary>
        private static string? TryReadSemanticModelPlatformDisplayName(ZipArchive zip)
        {
            const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
            var semanticModelPathMarker = SemanticModelFolderSuffix + "/";
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                if (!entry.Name.Equals(PlatformFileName, cmp))
                    continue;
                if (entry.FullName.IndexOf(semanticModelPathMarker, cmp) < 0)
                    continue;
                try
                {
                    using var stream = entry.Open();
                    using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var json = textReader.ReadToEnd();
                    var jo = JObject.Parse(json);
                    var displayName = jo["metadata"]?["displayName"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(displayName))
                        return displayName;
                }
                catch (JsonException)
                {
                    // ignore invalid .platform JSON
                }
            }
            return null;
        }

        private List<DataSourceConnection>? TryReadDataSourceConnectionsFromZip(byte[] zipBytes)
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read, leaveOpen: false);
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(ConnectionsJsonFileName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var datasources = JsonConvert.DeserializeObject<List<PowerBIDatasource>>(json);
                if (datasources == null || datasources.Count == 0)
                    return null;

                var mapped = datasources
                    .Where(ds => ds != null)
                    .Select(MapToDataSourceConnection)
                    .ToList();
                return mapped.Count > 0 ? mapped : null;
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Invalid {FileName} payload. Skipping data source connections mapping.", ConnectionsJsonFileName);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read {FileName}. Skipping data source connections mapping.", ConnectionsJsonFileName);
                return null;
            }
        }

        private static DataSourceConnection MapToDataSourceConnection(PowerBIDatasource datasource)
        {
            var parameters = BuildConnectionParameters(datasource.ConnectionDetails);
            var externalId = BuildExternalId(parameters);

            return new DataSourceConnection
            {
                Name = string.IsNullOrWhiteSpace(datasource.Name) ? Guid.NewGuid().ToString() : datasource.Name,
                Type = string.IsNullOrWhiteSpace(datasource.DatasourceType) ? "Unknown" : datasource.DatasourceType,
                Parameters = parameters.Count > 0 ? parameters : null,
                ExternalId = externalId
            };
        }

        private static Dictionary<string, string> BuildConnectionParameters(PowerBIDatasourceConnectionDetails? details)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (details == null)
                return parameters;

            AddParameterIfPresent(parameters, nameof(details.Server), details.Server);
            AddParameterIfPresent(parameters, nameof(details.Database), details.Database);
            AddParameterIfPresent(parameters, nameof(details.Url), details.Url);
            AddParameterIfPresent(parameters, nameof(details.Path), details.Path);
            AddParameterIfPresent(parameters, nameof(details.Kind), details.Kind);
            AddParameterIfPresent(parameters, nameof(details.Account), details.Account);
            AddParameterIfPresent(parameters, nameof(details.Domain), details.Domain);
            AddParameterIfPresent(parameters, nameof(details.EmailAddress), details.EmailAddress);
            AddParameterIfPresent(parameters, nameof(details.LoginServer), details.LoginServer);
            AddParameterIfPresent(parameters, nameof(details.ClassInfo), details.ClassInfo);

            return parameters;
        }

        private static void AddParameterIfPresent(Dictionary<string, string> parameters, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            parameters[key] = value;
        }

        private static string BuildExternalId(Dictionary<string, string> parameters)
        {
            if (parameters.Count == 0)
                return string.Empty;

            var values = parameters
                .OrderByDescending(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => kvp.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            return values.Length == 0 ? string.Empty : string.Join(";", values);
        }

        private (List<OpenBI.Table> tables, ICollection<Relationship>? schemaRelationships) ParseBimToOpenBI(Root root)
        {
            if (root?.model == null)
                return (new List<OpenBI.Table>(), new List<Relationship>());

            var tables = new List<OpenBI.Table>();
            if (root.model.tables != null)
            {
                foreach (Models.Schema.Table table in root.model.tables)
                {
                    var t = new OpenBI.Table
                    {
                        Name = table.name ?? "Table",
                        Id = table.lineageTag ?? Guid.NewGuid().ToString(),
                        Columns = new List<OpenBI.Column>()
                    };
                    if (table.partitions != null && table.partitions.Count > 0 && table.partitions[0].source != null)
                    {
                        var expr = table.partitions[0].source.expression;
                        string? exprCode = expr is JArray arr ? string.Join("\r\n", arr) : expr is string s ? s : null;
                        t.Expression = MakeExpression(exprCode, "M", "SourceQuery");
                    }
                    if (table.columns != null)
                    {
                        foreach (var col in table.columns)
                        {
                            t.Columns.Add(new OpenBI.Column
                            {
                                Id = col.lineageTag ?? Guid.NewGuid().ToString(),
                                Name = col.name ?? "Column",
                                DataType = GetColumnDataTypeFromBim(col.dataType),
                                Type = col.type ?? "column",
                                IsKey = col.isKey ?? false,
                                IsNullable = col.isNullable ?? false,
                                IsUnique = col.isUnique ?? false,
                                SummarizeBy = col.summarizeBy,
                                DataCategory = col.dataCategory,
                                Expression = MakeExpression(col.expression is string s2 ? s2 : col.expression is JArray jarr ? string.Join("\r\n", jarr) : null, "DAX", "CalculatedColumn"),
                                FormatString = col.formatString
                            });
                        }
                    }
                    if (table.measures != null)
                    {
                        foreach (var mea in table.measures)
                        {
                            var c = new OpenBI.Column
                            {
                                Type = "measure",
                                Name = mea.name ?? "Measure",
                                DataType = GetColumnDataTypeFromBim(mea.dataType),
                                IsKey = false,
                                IsNullable = false,
                                IsUnique = false,
                                Id = mea.lineageTag ?? Guid.NewGuid().ToString()
                            };
                            string? meaCode = mea.expression is JArray meaArr ? string.Join("\r\n", meaArr) : mea.expression is string meaStr ? meaStr : null;
                            c.Expression = MakeExpression(meaCode, "DAX", "Measure");
                            t.Columns.Add(c);
                        }
                    }
                    tables.Add(t);
                }
            }

            ICollection<Relationship> schemaRelationships = new List<Relationship>();
            if (root.model.relationships != null && root.model.relationships.Count > 0)
                schemaRelationships = ComputeRelationships(tables, root.model.relationships);

            return (tables, schemaRelationships);
        }

        private static ColumnDataType GetColumnDataTypeFromBim(string? dataType)
        {
            switch (dataType)
            {
                case "string": return ColumnDataType.String;
                case "int64": return ColumnDataType.Integer;
                case "decimal":
                case "double": return ColumnDataType.Decimal;
                case "dateTime": return ColumnDataType.Date;
                case "boolean": return ColumnDataType.Boolean;
                default: return ColumnDataType.Unknown;
            }
        }

        private static string ReverseColumnDataType(ColumnDataType dataType)
        {
            switch (dataType)
            {
                case ColumnDataType.String: return "string";
                case ColumnDataType.Integer: return "int64";
                case ColumnDataType.Decimal: return "double";
                case ColumnDataType.Date: return "dateTime";
                case ColumnDataType.Boolean: return "boolean";
                default: return "string";
            }
        }

        private static ICollection<Relationship> ComputeRelationships(ICollection<OpenBI.Table> tables, List<Models.Schema.Relationship> relationships)
        {
            var result = new List<Relationship>();
            if (relationships == null) return result;
            foreach (var r in relationships)
            {
                var tableFrom = tables.FirstOrDefault(x => string.Equals(x.Name, r.fromTable, StringComparison.OrdinalIgnoreCase));
                var columnFrom = tableFrom?.Columns?.FirstOrDefault(x => string.Equals(x.Name, r.fromColumn, StringComparison.OrdinalIgnoreCase));
                var tableTo = tables.FirstOrDefault(x => string.Equals(x.Name, r.toTable, StringComparison.OrdinalIgnoreCase));
                var columnTo = tableTo?.Columns?.FirstOrDefault(x => string.Equals(x.Name, r.toColumn, StringComparison.OrdinalIgnoreCase));
                if (columnFrom == null || columnTo == null)
                    continue;
                var direction = RelationshipDirection.OneToMany;
                if (r.fromCardinality == null && r.toCardinality == null)
                    direction = RelationshipDirection.ManyToOne;
                else if (r.fromCardinality == "one" && r.toCardinality == "one")
                    direction = RelationshipDirection.OneToOne;
                else if (r.fromCardinality == "one" && r.toCardinality == "many")
                    direction = RelationshipDirection.OneToMany;
                else if (r.fromCardinality == "many" && r.toCardinality == "one")
                    direction = RelationshipDirection.ManyToOne;
                else if (r.fromCardinality == "many" && r.toCardinality == "many")
                    direction = RelationshipDirection.ManyToMany;

                string idFrom;
                string idTo;
                if (direction == RelationshipDirection.OneToMany)
                {
                    idFrom = columnTo.Id;
                    idTo = columnFrom.Id;
                    direction = RelationshipDirection.ManyToOne;
                }
                else
                {
                    idFrom = columnFrom.Id;
                    idTo = columnTo.Id;
                }

                result.Add(new Relationship
                {
                    Name = r.name ?? $"Rel_{columnFrom.Id}_{columnTo.Id}",
                    IdColumnFrom = idFrom,
                    IdColumnTo = idTo,
                    Type = direction
                });
            }
            return result;
        }

        private (List<OpenBI.Table> tables, ICollection<Relationship> schemaRelationships, string? modelName) ParseTmdlToOpenBI(string definitionFolderPath)
        {
            if (!Directory.Exists(definitionFolderPath))
                throw new DirectoryNotFoundException($"Folder {definitionFolderPath} is not existing.");

            var tmdlModel = Microsoft.AnalysisServices.TmdlSerializer.DeserializeDatabaseFromFolder(definitionFolderPath);
            if (tmdlModel == null)
                throw new InvalidOperationException("TMDL model is null.");

            var modelName = tmdlModel.Model?.Name ?? tmdlModel.Name;
            var expressionProperty = typeof(Microsoft.AnalysisServices.Tabular.CalculatedColumn).GetProperty("Expression");

            #region Expressions (Functions and Parameters)

            var parametersTable = new Table()
            {
                Name = "Parameters",
                Id = "Parameters",
                Type = Table.TableTypeObject,
                Columns = new List<Column>()
            };

            var functionsTable = new Table()
            {
                Name = "Parameters",
                Id = "Parameters",
                Type = Table.TableTypeObject,
                Columns = new List<Column>()
            };

            if (tmdlModel.Model?.Expressions != null)
                foreach (var expression in tmdlModel.Model.Expressions)
                {
                    string type = null;

                    if (expression.Kind == Tom.ExpressionKind.M && (expression.Annotations?.Any(x => x.Name == "PBI_ResultType" && x.Value == "Function") ?? false))
                        type = "Function";
                    if (expression.Kind == Tom.ExpressionKind.M && expression.Expression.Contains("IsParameterQuery=true"))
                        type = "Parameter";

                    if (type == null)
                    {
                        _logger?.LogWarning($"Could not identify type of expression {expression.Name} ({expression.LineageTag})");

                        continue;
                    }

                    var openbiExpression = new Expression()
                    {
                        Language = Tom.ExpressionKind.M.ToString(),
                        Type = type,
                        Id = expression.LineageTag,
                        Code = expression.Expression
                    };

                    var column = new Column()
                    {
                        Description = expression.Description,
                        DataType = ColumnDataType.Unknown,
                        Expression = openbiExpression,
                        Id = expression.LineageTag,
                        Name = expression.Name,
                        Type = type
                    };

                    if (type == "Function")
                        functionsTable.Columns.Add(column);
                    else
                        parametersTable.Columns.Add(column);
                }

            #endregion

            var tables = new List<OpenBI.Table>();
            if (tmdlModel.Model?.Tables != null)
            {
                foreach (var table in tmdlModel.Model.Tables)
                {
                    var columns = new List<OpenBI.Column>();

                    if (table.Columns != null)
                    {
                        foreach (var tmdlCol in table.Columns)
                        {
                            var col = new OpenBI.Column
                            {
                                DataCategory = tmdlCol.DataCategory,
                                DataType = GetColumnDataTypeFromTmdl(tmdlCol.DataType),
                                FormatString = tmdlCol.FormatString,
                                Id = tmdlCol.LineageTag ?? Guid.NewGuid().ToString(),
                                IsKey = tmdlCol.IsKey,
                                IsNullable = tmdlCol.IsNullable,
                                IsUnique = tmdlCol.IsUnique,
                                Name = tmdlCol.Name ?? "Column",
                                Type = tmdlCol.Type.ToString()
                            };

                            if (tmdlCol.Type == Microsoft.AnalysisServices.Tabular.ColumnType.Calculated && expressionProperty != null)
                                col.Expression = MakeExpression(expressionProperty.GetValue(tmdlCol) as string, "DAX", "CalculatedColumn");
                            if (tmdlCol.SummarizeBy != null)
                                col.SummarizeBy = tmdlCol.SummarizeBy.ToString();
                            columns.Add(col);
                        }
                    }

                    if (table.Measures != null)
                    {
                        foreach (var measure in table.Measures)
                        {
                            columns.Add(new OpenBI.Column
                            {
                                Type = "measure",
                                Name = measure.Name ?? "Measure",
                                Id = measure.LineageTag ?? Guid.NewGuid().ToString(),
                                DataType = GetColumnDataTypeFromTmdl(measure.DataType),
                                IsKey = false,
                                IsNullable = false,
                                IsUnique = false,
                                Expression = MakeExpression(measure.Expression, "DAX", "Measure")
                            });
                        }
                    }

                    var partitionType = table.Partitions.FirstOrDefault()?.SourceType;
                    var partition = table.Partitions.FirstOrDefault()?.Source;
                    Expression? tableExpression = null;

                    switch (partitionType)
                    {
                        case null: break;
                        case Microsoft.AnalysisServices.Tabular.PartitionSourceType.Calculated:
                            tableExpression = MakeExpression((partition as Microsoft.AnalysisServices.Tabular.CalculatedPartitionSource)?.Expression, "DAX", "CalculatedTable");
                            break;
                        case Microsoft.AnalysisServices.Tabular.PartitionSourceType.M:
                            tableExpression = MakeExpression((partition as Microsoft.AnalysisServices.Tabular.MPartitionSource)?.Expression, "M", "SourceQuery");
                            break;
                        default: break;
                    }

                    tables.Add(new OpenBI.Table
                    {
                        Name = table.Name ?? "Table",
                        Id = table.LineageTag ?? Guid.NewGuid().ToString(),
                        Expression = tableExpression,
                        Columns = columns,
                        Type = "Table"
                    });
                }
            }

            var relationships = new List<Models.Schema.Relationship>();
            if (tmdlModel.Model?.Relationships != null)
            {
                foreach (var relationship in tmdlModel.Model.Relationships)
                {
                    var fromColumnProperty = relationship.GetType().GetProperty("FromColumn");
                    var toColumnProperty = relationship.GetType().GetProperty("ToColumn");
                    var cardinalityProperty = relationship.GetType().GetProperty("ToCardinality");
                    var fromColumn = fromColumnProperty?.GetValue(relationship) as Microsoft.AnalysisServices.Tabular.Column;
                    var toColumn = toColumnProperty?.GetValue(relationship) as Microsoft.AnalysisServices.Tabular.Column;
                    var card = cardinalityProperty?.GetValue(relationship) as Microsoft.AnalysisServices.Tabular.RelationshipEndCardinality?;

                    if (fromColumn == null || toColumn == null)
                        continue;

                    var relationshipMap = new Models.Schema.Relationship
                    {
                        name = relationship.Name,
                        fromTable = relationship.FromTable.Name,
                        fromColumn = fromColumn.Name,
                        toTable = relationship.ToTable.Name,
                        toColumn = toColumn.Name
                    };
                    if (!card.HasValue)
                    {
                        relationshipMap.fromCardinality = "one";
                        relationshipMap.toCardinality = "one";
                    }
                    else
                    {
                        switch (GetRelationshipCardinality(card.Value))
                        {
                            case RelationshipDirection.OneToOne:
                                relationshipMap.fromCardinality = "one";
                                relationshipMap.toCardinality = "one";
                                break;
                            case RelationshipDirection.OneToMany:
                                relationshipMap.fromCardinality = "one";
                                relationshipMap.toCardinality = "many";
                                break;
                            case RelationshipDirection.ManyToMany:
                                relationshipMap.fromCardinality = "many";
                                relationshipMap.toCardinality = "many";
                                break;
                            case RelationshipDirection.ManyToOne:
                                relationshipMap.fromCardinality = "many";
                                relationshipMap.toCardinality = "one";
                                break;
                            default:
                                relationshipMap.fromCardinality = "many";
                                relationshipMap.toCardinality = "one";
                                break;
                        }
                    }
                    relationships.Add(relationshipMap);
                }
            }

            var schemaRelationships = ComputeRelationships(tables, relationships);
            return (tables, schemaRelationships, modelName);
        }

        private static ColumnDataType GetColumnDataTypeFromTmdl(Microsoft.AnalysisServices.Tabular.DataType dataType)
        {
            switch (dataType)
            {
                case Microsoft.AnalysisServices.Tabular.DataType.String:
                    return ColumnDataType.String;
                case Microsoft.AnalysisServices.Tabular.DataType.Int64:
                    return ColumnDataType.Integer;
                case Microsoft.AnalysisServices.Tabular.DataType.Decimal:
                case Microsoft.AnalysisServices.Tabular.DataType.Double:
                    return ColumnDataType.Decimal;
                case Microsoft.AnalysisServices.Tabular.DataType.DateTime:
                    return ColumnDataType.Date;
                case Microsoft.AnalysisServices.Tabular.DataType.Boolean:
                    return ColumnDataType.Boolean;
                case Microsoft.AnalysisServices.Tabular.DataType.Automatic:
                case Microsoft.AnalysisServices.Tabular.DataType.Binary:
                case Microsoft.AnalysisServices.Tabular.DataType.Variant:
                case Microsoft.AnalysisServices.Tabular.DataType.Unknown:
                default:
                    return ColumnDataType.Unknown;
            }
        }

        private static RelationshipDirection GetRelationshipCardinality(Microsoft.AnalysisServices.Tabular.RelationshipEndCardinality cardinalityTo)
        {
            switch (cardinalityTo)
            {
                case Microsoft.AnalysisServices.Tabular.RelationshipEndCardinality.One:
                    return RelationshipDirection.ManyToOne;
                case Microsoft.AnalysisServices.Tabular.RelationshipEndCardinality.Many:
                    return RelationshipDirection.ManyToMany;
                default:
                    return RelationshipDirection.ManyToOne;
            }
        }

        private byte[] BuildModelBimBytes(Asset asset, byte[]? existingModelBimBytes)
        {
            var root = LoadTemplateRoot(existingModelBimBytes);
            ApplySemanticModelFromAsset(root, asset);
            var json = root.ToString(Formatting.None);
            return Encoding.UTF8.GetBytes(json);
        }

        private JObject LoadTemplateRoot(byte[]? existingModelBimBytes)
        {
            string json;
            if (existingModelBimBytes != null && existingModelBimBytes.Length > 0)
            {
                json = Encoding.UTF8.GetString(existingModelBimBytes);
            }
            else
            {
                json = LoadEmbeddedResource(DefaultSemanticModelTemplateResourceName);
            }
            var root = JObject.Parse(json);
            if (root["model"]?["tables"] == null)
                root["model"]!["tables"] = new JArray();
            if (root["model"]?["relationships"] == null)
                root["model"]!["relationships"] = new JArray();
            return root;
        }

        private static string LoadEmbeddedResource(string resourceName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                var names = assembly.GetManifestResourceNames();
                throw new InvalidOperationException($"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", names)}");
            }
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private void ApplySemanticModelFromAsset(JObject root, Asset asset)
        {
            var tables = (ICollection<OpenBI.Table>)(asset.DataModel?.Tables ?? new List<OpenBI.Table>());
            var relationships = (ICollection<Relationship>)(asset.DataModel?.Relationships ?? new List<Relationship>());
            var model = root["model"] as JObject;
            if (model == null) return;

            var tablesArray = new JArray();
            var columnIdToTableAndName = new Dictionary<string, (string tableName, string columnName)>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                var tableName = table.Name ?? "Table";
                var tableId = table.Id ?? Guid.NewGuid().ToString();
                var columnsArray = new JArray();
                var measuresArray = new JArray();
                foreach (var col in table.Columns ?? Array.Empty<OpenBI.Column>())
                {
                    var colId = col.Id ?? Guid.NewGuid().ToString();
                    columnIdToTableAndName[colId] = (tableName, col.Name ?? "Column");
                    if (string.Equals(col.Type, "measure", StringComparison.OrdinalIgnoreCase))
                    {
                        measuresArray.Add(new JObject
                        {
                            ["name"] = col.Name ?? "Measure",
                            ["lineageTag"] = colId,
                            ["dataType"] = ReverseColumnDataType(col.DataType),
                            ["expression"] = string.IsNullOrEmpty(GetCode(col.Expression)) ? "0" : GetCode(col.Expression)
                        });
                    }
                    else
                    {
                        var colObj = new JObject
                        {
                            ["name"] = col.Name ?? "Column",
                            ["lineageTag"] = colId,
                            ["dataType"] = ReverseColumnDataType(col.DataType),
                            ["isKey"] = col.IsKey,
                            ["isNullable"] = col.IsNullable,
                            ["isUnique"] = col.IsUnique
                        };

                        if (string.Equals(col.Type, "calculated", StringComparison.OrdinalIgnoreCase))
                            colObj["type"] = "calculated";

                        if (!string.IsNullOrEmpty(col.SummarizeBy)) colObj["summarizeBy"] = col.SummarizeBy;
                        if (!string.IsNullOrEmpty(col.DataCategory)) colObj["dataCategory"] = col.DataCategory;
                        if (!string.IsNullOrEmpty(col.FormatString)) colObj["formatString"] = col.FormatString;
                        if (!string.IsNullOrEmpty(GetCode(col.Expression))) colObj["expression"] = GetCode(col.Expression);
                        columnsArray.Add(colObj);
                    }
                }
                var sourceExpression = !string.IsNullOrEmpty(GetCode(table.Expression)) ? GetCode(table.Expression) : "\"\"";
                var partition = new JObject
                {
                    ["name"] = "Partition",
                    ["mode"] = "import",
                    ["state"] = "ready",
                    ["source"] = new JObject
                    {
                        ["type"] = "m",
                        ["expression"] = sourceExpression
                    }
                };
                tablesArray.Add(new JObject
                {
                    ["name"] = tableName,
                    ["lineageTag"] = tableId,
                    ["columns"] = columnsArray,
                    ["measures"] = measuresArray,
                    ["partitions"] = new JArray(partition)
                });
            }
            root["model"]["tables"] = tablesArray;

            var relationshipsArray = new JArray();
            foreach (var rel in relationships)
            {
                if (!columnIdToTableAndName.TryGetValue(rel.IdColumnFrom, out var from) ||
                    !columnIdToTableAndName.TryGetValue(rel.IdColumnTo, out var to))
                    continue;

                string fromCard;
                string toCard;
                string fromTable;
                string fromColumn;
                string toTable;
                string toColumn;
                if (rel.Type == RelationshipDirection.OneToMany)
                {
                    fromTable = to.tableName;
                    fromColumn = to.columnName;
                    toTable = from.tableName;
                    toColumn = from.columnName;
                    fromCard = "many";
                    toCard = "one";
                }
                else
                {
                    fromTable = from.tableName;
                    fromColumn = from.columnName;
                    toTable = to.tableName;
                    toColumn = to.columnName;
                    (fromCard, toCard) = RelationshipDirectionToCardinality(rel.Type);
                }

                relationshipsArray.Add(new JObject
                {
                    ["name"] = rel.Name ?? $"Rel_{from.tableName}_{from.columnName}_to_{to.tableName}_{to.columnName}",
                    ["fromTable"] = fromTable,
                    ["fromColumn"] = fromColumn,
                    ["toTable"] = toTable,
                    ["toColumn"] = toColumn,
                    ["fromCardinality"] = fromCard,
                    ["toCardinality"] = toCard
                });
            }
            root["model"]["relationships"] = relationshipsArray;
        }

        private static (string fromCardinality, string toCardinality) RelationshipDirectionToCardinality(RelationshipDirection direction)
        {
            switch (direction)
            {
                case RelationshipDirection.OneToOne: return ("one", "one");
                case RelationshipDirection.OneToMany: return ("one", "many");
                case RelationshipDirection.ManyToOne: return ("many", "one");
                case RelationshipDirection.ManyToMany: return ("many", "many");
                default: return ("many", "one");
            }
        }

        private static Microsoft.AnalysisServices.Database BuildTabularDatabaseFromAsset(Asset asset, string safeAssetName)
        {
            var modelName = string.IsNullOrWhiteSpace(asset.Info?.Name) ? safeAssetName : asset.Info!.Name;
            var db = new Microsoft.AnalysisServices.Database
            {
                Name = SanitizeTomIdentifier(safeAssetName),
                CompatibilityLevel = 1550,
                Model = new Tom.Model
                {
                    Name = SanitizeTomIdentifier(modelName),
                    Culture = "en-US",
                    DefaultPowerBIDataSourceVersion = Tom.PowerBIDataSourceVersion.PowerBI_V3
                }
            };
            var model = db.Model!;
            var columnIdToTabularColumn = new Dictionary<string, Tom.Column>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in asset.DataModel?.Tables ?? new List<OpenBI.Table>())
            {
                var tableName = table.Name ?? "Table";
                var tomTable = new Tom.Table
                {
                    Name = tableName,
                    LineageTag = NormalizeLineageTag(table.Id)
                };

                foreach (var col in table.Columns ?? Array.Empty<OpenBI.Column>())
                {
                    var colId = col.Id ?? Guid.NewGuid().ToString();
                    if (string.Equals(col.Type, "measure", StringComparison.OrdinalIgnoreCase))
                    {
                        var measure = new Tom.Measure
                        {
                            Name = col.Name ?? "Measure",
                            LineageTag = NormalizeLineageTag(colId),
                            Expression = string.IsNullOrEmpty(GetCode(col.Expression)) ? "0" : GetCode(col.Expression)!
                        };
                        tomTable.Measures.Add(measure);
                        continue;
                    }

                    if (string.Equals(col.Type, "calculated", StringComparison.OrdinalIgnoreCase))
                    {
                        var calc = new Tom.CalculatedColumn
                        {
                            Name = col.Name ?? "Column",
                            LineageTag = NormalizeLineageTag(colId),
                            DataType = MapOpenBiDataTypeToTabular(col.DataType),
                            IsKey = col.IsKey,
                            IsNullable = col.IsNullable,
                            IsUnique = col.IsUnique,
                            FormatString = col.FormatString,
                            DataCategory = col.DataCategory,
                            Expression = GetCode(col.Expression) ?? string.Empty
                        };
                        ApplySummarizeByIfPresent(calc, col.SummarizeBy);
                        tomTable.Columns.Add(calc);
                        columnIdToTabularColumn[colId] = calc;
                        continue;
                    }

                    var dataCol = new Tom.DataColumn
                    {
                        Name = col.Name ?? "Column",
                        LineageTag = NormalizeLineageTag(colId),
                        DataType = MapOpenBiDataTypeToTabular(col.DataType),
                        IsKey = col.IsKey,
                        IsNullable = col.IsNullable,
                        IsUnique = col.IsUnique,
                        FormatString = col.FormatString,
                        DataCategory = col.DataCategory
                    };
                    ApplySummarizeByIfPresent(dataCol, col.SummarizeBy);
                    tomTable.Columns.Add(dataCol);
                    columnIdToTabularColumn[colId] = dataCol;
                }

                var mExpr = string.IsNullOrEmpty(GetCode(table.Expression)) ? "\"\"" : GetCode(table.Expression)!;
                tomTable.Partitions.Add(new Tom.Partition
                {
                    Name = "Partition",
                    Mode = Tom.ModeType.Import,
                    Source = new Tom.MPartitionSource { Expression = mExpr }
                });

                model.Tables.Add(tomTable);
            }

            foreach (var rel in asset.DataModel?.Relationships ?? new List<Relationship>())
            {
                if (!TryResolveRelationshipToTomEndpoints(rel, columnIdToTabularColumn, out var fromCol, out var toCol, out var fromCard, out var toCard, out var relName))
                    continue;

                var single = new Tom.SingleColumnRelationship
                {
                    Name = relName,
                    FromColumn = fromCol,
                    ToColumn = toCol,
                    FromCardinality = StringCardinalityToRelationshipEnd(fromCard),
                    ToCardinality = StringCardinalityToRelationshipEnd(toCard)
                };
                model.Relationships.Add(single);
            }

            return db;
        }

        private static void ApplySummarizeByIfPresent(Tom.Column column, string? summarizeBy)
        {
            if (string.IsNullOrWhiteSpace(summarizeBy))
                return;
            if (Enum.TryParse<Tom.AggregateFunction>(summarizeBy, ignoreCase: true, out var agg))
                column.SummarizeBy = agg;
        }

        private static bool TryResolveRelationshipToTomEndpoints(
            Relationship rel,
            Dictionary<string, Tom.Column> columnIdToColumn,
            out Tom.Column fromCol,
            out Tom.Column toCol,
            out string fromCard,
            out string toCard,
            out string relName)
        {
            fromCol = null!;
            toCol = null!;
            fromCard = toCard = string.Empty;
            relName = rel.Name ?? string.Empty;

            if (!columnIdToColumn.TryGetValue(rel.IdColumnFrom, out var fromColumnObj) ||
                !columnIdToColumn.TryGetValue(rel.IdColumnTo, out var toColumnObj))
                return false;

            if (rel.Type == RelationshipDirection.OneToMany)
            {
                fromCol = toColumnObj;
                toCol = fromColumnObj;
                fromCard = "many";
                toCard = "one";
            }
            else
            {
                fromCol = fromColumnObj;
                toCol = toColumnObj;
                (fromCard, toCard) = RelationshipDirectionToCardinality(rel.Type);
            }

            if (string.IsNullOrEmpty(relName))
                relName = $"Rel_{fromCol.Name}_{toCol.Name}";
            return true;
        }

        private static Tom.RelationshipEndCardinality StringCardinalityToRelationshipEnd(string card) =>
            string.Equals(card, "one", StringComparison.OrdinalIgnoreCase)
                ? Tom.RelationshipEndCardinality.One
                : Tom.RelationshipEndCardinality.Many;

        private static Tom.DataType MapOpenBiDataTypeToTabular(ColumnDataType dataType)
        {
            switch (dataType)
            {
                case ColumnDataType.String: return Tom.DataType.String;
                case ColumnDataType.Integer: return Tom.DataType.Int64;
                case ColumnDataType.Decimal: return Tom.DataType.Double;
                case ColumnDataType.Date:
                case ColumnDataType.Timestamp: return Tom.DataType.DateTime;
                case ColumnDataType.Boolean: return Tom.DataType.Boolean;
                default: return Tom.DataType.String;
            }
        }

        private static string NormalizeLineageTag(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return Guid.NewGuid().ToString();
            return Guid.TryParse(id, out var g) ? g.ToString() : id;
        }

        private static string SanitizeTomIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return DefaultAssetName;
            var trimmed = name.Trim();
            return trimmed.Length > 0 ? trimmed : DefaultAssetName;
        }

        /// <summary>
        /// Minimal definition.pbism required by Fabric API for semantic model (TMSL in model.bim).
        /// See https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-dataset#definitionpbism
        /// </summary>
        private static readonly string DefinitionPbismContent = "{\"version\":\"4.0\",\"settings\":{}}";

        private static byte[] BuildSemanticModelZip(string assetName, byte[] modelBimBytes, AssetInfo info)
        {
            var folderName = assetName.EndsWith(SemanticModelFolderSuffix, StringComparison.OrdinalIgnoreCase)
                ? assetName
                : assetName + SemanticModelFolderSuffix;
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var bimEntry = zip.CreateEntry($"{folderName}/{ModelBimFileName}");
                using (var es = bimEntry.Open())
                    es.Write(modelBimBytes, 0, modelBimBytes.Length);
                var definitionPbismEntry = zip.CreateEntry($"{folderName}/{DefinitionPbismFileName}");
                using (var es = definitionPbismEntry.Open())
                {
                    var pbismBytes = Encoding.UTF8.GetBytes(DefinitionPbismContent);
                    es.Write(pbismBytes, 0, pbismBytes.Length);
                }
                var infoJson = JsonConvert.SerializeObject(new
                {
                    id = info.Id,
                    name = info.Name,
                    description = info.Description ?? string.Empty
                }, Formatting.None);
                var infoEntry = zip.CreateEntry($"{folderName}/{InfoJsonFileName}");
                using (var es = infoEntry.Open())
                {
                    var infoBytes = Encoding.UTF8.GetBytes(infoJson);
                    es.Write(infoBytes, 0, infoBytes.Length);
                }
                var platform = new JObject
                {
                    ["$schema"] = PlatformSchema,
                    ["metadata"] = new JObject
                    {
                        ["type"] = "SemanticModel",
                        ["displayName"] = info.Name ?? assetName
                    },
                    ["config"] = new JObject
                    {
                        ["version"] = "2.0",
                        ["logicalId"] = !string.IsNullOrWhiteSpace(info.Id) ? info.Id : Guid.NewGuid().ToString()
                    }
                };
                var platformJson = platform.ToString();
                var platformEntry = zip.CreateEntry($"{folderName}/{PlatformFileName}");
                using (var es = platformEntry.Open())
                {
                    var platformBytes = Encoding.UTF8.GetBytes(platformJson);
                    es.Write(platformBytes, 0, platformBytes.Length);
                }
            }
            ms.Position = 0;
            return ms.ToArray();
        }

        /// <summary>
        /// Builds the same semantic model zip layout as <see cref="BuildSemanticModelZip(string, byte[], AssetInfo)"/>,
        /// but embeds a TMDL <c>definition</c> tree instead of <c>model.bim</c>.
        /// </summary>
        private static byte[] BuildSemanticModelZipFromDefinitionFolder(string assetName, string definitionRootPath, AssetInfo info)
        {
            var folderName = assetName.EndsWith(SemanticModelFolderSuffix, StringComparison.OrdinalIgnoreCase)
                ? assetName
                : assetName + SemanticModelFolderSuffix;
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in Directory.EnumerateFiles(definitionRootPath, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(definitionRootPath, filePath);
                    var entryName = $"definition/{relative.Replace(Path.DirectorySeparatorChar, '/')}";
                    var entry = zip.CreateEntry(entryName);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(filePath);
                    fileStream.CopyTo(entryStream);
                }

                var definitionPbismEntry = zip.CreateEntry($"{DefinitionPbismFileName}");
                using (var es = definitionPbismEntry.Open())
                {
                    var pbismBytes = Encoding.UTF8.GetBytes(DefinitionPbismContent);
                    es.Write(pbismBytes, 0, pbismBytes.Length);
                }
                var infoJson = JsonConvert.SerializeObject(new
                {
                    id = info.Id,
                    name = info.Name,
                    description = info.Description ?? string.Empty
                }, Formatting.None);
                var infoEntry = zip.CreateEntry($"{InfoJsonFileName}");
                using (var es = infoEntry.Open())
                {
                    var infoBytes = Encoding.UTF8.GetBytes(infoJson);
                    es.Write(infoBytes, 0, infoBytes.Length);
                }
                var platform = new JObject
                {
                    ["$schema"] = PlatformSchema,
                    ["metadata"] = new JObject
                    {
                        ["type"] = "SemanticModel",
                        ["displayName"] = info.Name ?? assetName
                    },
                    ["config"] = new JObject
                    {
                        ["version"] = "4.0",
                        ["logicalId"] = !string.IsNullOrWhiteSpace(info.Id) ? info.Id : Guid.NewGuid().ToString()
                    }
                };
                var platformJson = platform.ToString();
                var platformEntry = zip.CreateEntry($"{PlatformFileName}");
                using (var es = platformEntry.Open())
                {
                    var platformBytes = Encoding.UTF8.GetBytes(platformJson);
                    es.Write(platformBytes, 0, platformBytes.Length);
                }
            }
            ms.Position = 0;
            return ms.ToArray();
        }
    }
}
