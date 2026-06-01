# PowerBI Semantic Model — Patch Support

**Converter:** `PowerBISemanticModelOpenBIConverter.FromOpenBIPatchArtifactAsync`  
**File:** `PowerBISemanticModelOpenBIConverter.cs`

---

## Artifact Format Requirement

| Format | Supported |
|--------|-----------|
| TMDL (`definition/` folder in ZIP) | ✅ |
| BIM (`model.bim` in ZIP) | ❌ throws `NotSupportedException` |

BIM artifacts must be fully re-converted via `FromOpenBIToArtifactAsync`. If the artifact contains
both a `model.bim` and a `definition/` folder, BIM takes precedence in the detection logic and the
patch will throw.

---

## Supported Entities

### `Table`

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Creates a new TOM table with an M partition. All columns and measures in `ValueJson` are added. Partition type is always M (import mode). |
| Remove | ✅ | Removes the table and all its columns, measures, and the partition. Existing relationships referencing columns in the removed table will break — no cascade cleanup is performed. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | Renames the TOM table directly. |
| `expression` | ✅ | Updates the M expression on the first M partition. If the table has a `CalculatedPartitionSource` (DAX calculated table), updates that instead. If no updatable partition is found, records an error for this part. |
| `type` | ⛔ skipped | OpenBI-only field, not mapped to TOM. |
| `additionalMetadata` | ⛔ skipped | OpenBI-only field, not mapped to TOM. |

---

### `Column` (DataColumn, CalculatedColumn, Measure)

The patch distinguishes column type by searching `table.Columns` (DataColumn / CalculatedColumn)
vs `table.Measures` (Measure) by `LineageTag`.

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Requires `ParentId` = table's `LineageTag`. Column `Type` field drives which TOM object is created: `"measure"` → `Tom.Measure`, `"calculated"` → `Tom.CalculatedColumn`, anything else → `Tom.DataColumn`. |
| Remove | ✅ | Searches all tables by `LineageTag`. Removes from either `Columns` or `Measures` collection. |
| Replace | ✅ (partial) | See property tables below. |

**Replace properties — DataColumn / CalculatedColumn:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | |
| `dataType` | ✅ | Deserializes `ColumnDataType` enum, maps to `Tom.DataType`. |
| `isKey` | ✅ | |
| `isNullable` | ✅ | |
| `isUnique` | ✅ | |
| `formatString` | ✅ | |
| `dataCategory` | ✅ | |
| `summarizeBy` | ✅ | Parses value as `Tom.AggregateFunction`. If value is null or unrecognized, no change is made. |
| `expression` | ✅ CalculatedColumn only | Ignored for DataColumn — a regular column cannot have a DAX expression. |
| `type` | ⛔ skipped | Changing column type (e.g. DataColumn → CalculatedColumn) is not supported — it would require removing and re-adding the column with a different TOM type. |
| `description` | ⛔ skipped | Not mapped to TOM in this converter. |
| `isDimension` / `isMeasure` | ⛔ skipped | OpenBI-only fields. |
| `columnsReferences` | ⛔ skipped | OpenBI-only field. |
| `additionalMetadata` | ⛔ skipped | OpenBI-only field. |

**Replace properties — Measure:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | |
| `expression` | ✅ | Updates the DAX expression. Empty / null code defaults to `"0"`. |
| `dataType` | ⛔ skipped | `Tom.Measure.DataType` is computed by the Analysis Services engine from the expression result type — it is read-only and cannot be set externally. |

---

### `Relationship`

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Both `IdColumnFrom` and `IdColumnTo` must already exist in the model as columns (not measures). Keyed by `Name`. |
| Remove | ✅ | Keyed by `Name` (case-insensitive). |
| Replace | ✅ | Internally implemented as **Remove + Add**. The current TOM state is reconstructed, changed parts are applied, then the old relationship is removed and a new one is created. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | Triggers Remove + Add internally. |
| `idColumnFrom` | ✅ | Triggers Remove + Add internally. New column must already exist in the model. |
| `idColumnTo` | ✅ | Triggers Remove + Add internally. New column must already exist in the model. |
| `type` | ✅ | `RelationshipDirection` enum. Triggers Remove + Add internally. |
| `expression` | ⛔ skipped | Not applicable to TOM `SingleColumnRelationship`. |
| `additionalMetadata` | ⛔ skipped | OpenBI-only field. |

> **⚠️ Order dependency:** if a Relationship Replace references a column introduced by a preceding
> Add in the same batch, the Add must appear before the Replace in the change list. The comparer
> always produces changes in a safe order (Adds before Replaces/Removes).

---

## Unsupported Entities

The following entities produce a **collected error** (not a throw) and are skipped:

| Entity | Reason |
|--------|--------|
| `AssetInfo` | Name / metadata is embedded in the ZIP's `info.json` and `.platform` files, which are rebuilt from the original artifact. Renaming via patch is not supported — re-upload with a new name via `FromOpenBIToArtifactAsync`. |
| `Page` | Semantic models have no report pages. The `Layout` is always empty. |
| `Visual` | Same as Page — no layout. |
| `VisualProjection` | Same as Page — no layout. |
| `Filter` | Same as Page — no layout. |
| `RefreshTask` | Refresh schedules are managed via the Power BI REST API separately from the artifact content. Not stored in TMDL. |
| `RefreshTrigger` | Same as `RefreshTask`. |
| `DataSourceConnection` | Stored in `connections.json` inside the ZIP, which is preserved unchanged from the original artifact. Not patched. |

---

## Error Handling Behaviour

- Errors are **collected per change**, not thrown. The returned `OpenBIPatchResult.Artifact`
  reflects all successfully applied changes even when some fail.
- `OpenBIPatchResult.IsSuccess` is `false` when any error is collected.
- The MCP server (`upload_asset`) will **refuse to upload** if `IsSuccess` is `false` — it returns
  the error list to the LLM for inspection instead.
- A change that fails mid-way (e.g. a Replace where some parts succeed and one fails) applies all
  successfully processed parts before the failing one; the failing part is recorded as an error on
  that specific property.

---

## Known Limitations

1. **Changing column type (DataColumn ↔ CalculatedColumn)** is not supported via Replace.
   Workaround: Remove the old column and Add a new one with the desired type in the same change
   batch.

2. **Table with no partition** (edge case in malformed TMDL): attempting to patch the `expression`
   on such a table will collect an error for that part and leave the table unchanged.

3. **Relationship cardinality reconstruction**: when a Relationship Replace is processed, the
   current state is reconstructed from TOM by reading `FromCardinality` / `ToCardinality`. If a
   third-party tool wrote the relationship in a non-standard direction, the reconstructed
   `IdColumnFrom` / `IdColumnTo` may be swapped relative to what was originally in the OpenBI
   model. Round-trips through `FromArtifactToOpenBIAsync` → `Compare` → patch are stable because
   the comparer always works with the converter's own output.

4. **No DAX validation**: after adding a measure or changing a DAX expression, the TMDL is
   serialized as-is with no expression validation. Invalid DAX will serialize and upload without
   error but will fail when Power BI processes the model.

5. **No cascade on Table Remove**: removing a table does not automatically remove relationships
   that reference columns in that table. Orphaned relationships will cause a TMDL serialization
   error or a platform rejection on upload.
