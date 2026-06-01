# PowerBI PBIR Report — Patch Support

**Converter:** `PowerBIReportPbirOpenBIConverter.FromOpenBIPatchArtifactAsync`  
**File:** `PowerBIReportPbirOpenBIConverter.cs`

Patch applies changes **directly to JSON files inside the report ZIP** (`.platform`, `pages.json`, `page.json`,
`visual.json`, `reportExtensions.json`). Unmodified entries (`definition.pbir`, `report.json`, `version.json`,
bookmarks, semantic-model binding, etc.) are preserved as-is from the original artifact.

---

## Artifact Format Requirement

| Format | Supported |
|--------|-----------|
| PBIR (`definition.pbir` in ZIP) | ✅ |
| Legacy PBIX / Report-Legacy layout | ❌ throws `NotSupportedException` |

Legacy artifacts must be fully re-converted via `FromOpenBIToArtifactAsync` (which produces PBIR when the
converter handles the asset). `FromArtifactToOpenBIAsync` still delegates legacy artifacts to the fallback
converter, but **patch does not**.

---

## Supported Entities

### `AssetInfo`

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ❌ | Collected error: only Replace supported. |
| Remove | ❌ | Collected error: only Replace supported. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | Maps to `.platform` → `metadata.displayName`. |
| `description` | ⛔ skipped | Not written by patch. |
| `externalType` | ⛔ skipped | |
| `idFolder` | ⛔ skipped | |
| `folderName` | ⛔ skipped | |
| `type` | ⛔ skipped | |
| `additionalMetadata` | ⛔ skipped | Semantic-model binding metadata lives in `definition.pbir` and is not patched. |

---

### `Page`

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Creates `definition/pages/{pageId}/page.json`. Appends `pageId` to `pages.json` → `pageOrder` **only if** `pages.json` already exists and has a `pageOrder` array. |
| Remove | ✅ | Deletes all ZIP entries under `definition/pages/{pageId}/`. Removes `pageId` from `pageOrder`; if it was `activePage`, sets `activePage` to the first remaining page. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | Maps to `page.json` → `displayName`. |
| `width` | ✅ | |
| `height` | ✅ | |
| `isEnabled` | ✅ | `true` removes `visibility`; `false` sets `visibility` = `"HiddenInViewMode"`. |
| `order` | ✅ | Reorders entry in `pages.json` → `pageOrder` (clamped to valid range). |
| `description` | ⛔ skipped | OpenBI mirrors `displayName` on read; not stored separately in `page.json`. |
| `embedPageUrlParameter` | ⛔ skipped | OpenBI-only / derived (`EmbedPageUrlParameter` = page id on read). |
| `embedPageUrl` | ⛔ skipped | Not stored in PBIR page JSON. |
| `additionalMetadata` | ⛔ skipped | |

**Page Add defaults:** `displayOption` = `"FitToPage"`, width 1280, height 720. `pageId` taken from `ValueJson.Id`
or generated as `page-{n}`.

---

### `Visual`

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Requires `ParentId` = page id. Writes `definition/pages/{pageId}/visuals/{visualId}/visual.json`. PBIR discovers visuals by folder layout — no separate registry entry. |
| Remove | ✅ | Removes the `visual.json` ZIP entry. Does not delete sibling files under the visual folder if present. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | `visual.json` → `name`. |
| `type` | ✅ | `visual.json` → `visual.visualType`. |
| `x` / `y` / `z` | ✅ | `visual.json` → `position.*`. |
| `width` / `height` | ✅ | `visual.json` → `position.width` / `position.height`. |
| `additionalMetadata` | ✅ (partial) | Only two keys are mapped back into `visual.json`: `objects` and `visualContainerObjects`. Other metadata entries are ignored. Malformed JSON is skipped silently. |
| `category` | ⛔ skipped | Not stored separately in `visual.json`. |
| `openBIVisualType` | ⛔ skipped | OpenBI-only field. |
| `description` | ⛔ skipped | Not stored separately in `visual.json`. |

**Visual Add defaults:** `visualType` defaults to `"visualContainer"`, position defaults (0,0,0 / 240×160),
`drillFilterOtherVisuals` = `true`. If `ValueJson.VisualProjections` is present, builds `visual.query.queryState`
from it (same shape as projection patch Add).

---

### `VisualProjection`

Identity in patch operations uses a **composite key** (see `VisualProjectionKey`):

```
{visualId}::{projectionName}::{order}
```

`order` is **1-based** (matches OpenBI `Order` on read). `projectionName` is the PBIR query-state bucket name
(`ProjectionName` on read — e.g. `"Values"`, `"Category"`).

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Requires `ParentId` = visual id. Requires `ProjectionName` and `IdColumnReference` in `ValueJson`. Appends to `visual.query.queryState.{bucket}.projections`. Creates `query` / `queryState` path if missing. |
| Remove | ✅ | Removes projection at index `order - 1` within the bucket. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `isActive` | ✅ | Sets or removes `active` on the projection object. |
| `idColumnReference` | ✅ | Rebuilds `field`, `queryRef`, and `nativeQueryRef`. Preserves Measure vs Column from the **existing** projection (`field.Measure` presence); does not accept a new `isMeasure` part. |
| `order` | ✅ | Moves projection within the same bucket (1-based index, clamped). |
| `openBIProjectionName` | ⛔ skipped | OpenBI-only normalized role name from the visual-type catalog. PBIR query-state buckets use platform `ProjectionName` — not patchable via this property. |
| `isDimension` | ⛔ skipped | Derived on read; not written independently. |
| `isMeasure` | ⛔ skipped | Use Add with correct `IsMeasure` in `ValueJson`, or rely on existing field shape on Replace of `idColumnReference`. |
| `type` | ⛔ skipped | |
| `expression` | ⛔ skipped | Implicit aggregated measures (expression-only, no `IdColumnReference`) are not representable in patch Add/Replace. |
| `additionalMetadata` | ⛔ skipped | |

**Query reference format:** `IdColumnReference` is expected as `Entity.Property` (e.g. `Sales.Amount`).
Aggregation wrappers in references (`Sum(...)`, `Avg(...)`, etc.) are stripped when building the field
`SourceRef`. Unparseable references fall back to an `Unknown` entity stub (same as full conversion write path).

> **⚠️ Legacy visual shape:** `FromArtifactToOpenBIAsync` also reads legacy `singleVisual.projections` /
> `prototypeQuery.Select`, but patch **only writes** the PBIR `query.queryState` shape. Patching a visual that
> still uses the legacy layout will migrate projections to the modern shape on first projection change.

> **⚠️ Order dependency:** VisualProjection Add/Replace on a visual introduced in the same batch requires the
> Visual Add to appear first. The comparer orders Adds before Replaces/Removes.

---

### `Table` (Report Extension entity)

In OpenBI, report-level DAX measures are exposed as `DataModel.Tables[]` where each table is a
`TableTypeObject` entity from `reportExtensions.json`. This is **not** the semantic model — it is the report's
local measure extension file.

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Creates `{ name, measures: [] }` in `reportExtensions.json` → `entities`. Creates the file with schema defaults if missing. Does **not** add columns from nested `ValueJson`. |
| Remove | ✅ | Removes entity by `Id` (= entity name). No error if `reportExtensions.json` is absent. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | Renames the entity in `reportExtensions.json`. |
| `type` | ⛔ skipped | Must remain `TableTypeObject`; not validated on patch. |
| `expression` | ⛔ skipped | Not applicable to extension entities. |
| `additionalMetadata` | ⛔ skipped | |

---

### `Column` (Report Extension measure)

Only **report extension measures** are supported. On read, non-measure columns and entities without expression
are omitted from OpenBI.

| Operation | Supported | Notes |
|-----------|-----------|-------|
| Add | ✅ | Requires `ParentId` = table (entity) name. Appends to `entities[].measures`. |
| Remove | ✅ | Removes measure by `Id` (= measure name) across all entities. No error if file absent. |
| Replace | ✅ (partial) | See property table below. |

**Replace properties:**

| Property | Supported | Notes |
|----------|-----------|-------|
| `name` | ✅ | |
| `expression` | ✅ | Expects `ValueJson` as `{ "Code": "..." }` (OpenBI Expression shape). Null / missing `Code` leaves expression unchanged. |
| `dataType` | ✅ | Maps OpenBI `ColumnDataType` enum to report-extension primitive type string. |
| `description` | ✅ | |
| `type` | ⛔ skipped | Must be `"measure"`; changing to/from other column types is not supported. |
| `summarizeBy` | ⛔ skipped | Not stored in report extensions. |
| `formatString` | ⛔ skipped | |
| `dataCategory` | ⛔ skipped | |
| `isKey` / `isUnique` / `isNullable` | ⛔ skipped | |
| `isDimension` / `isMeasure` | ⛔ skipped | |
| `columnsReferences` | ⛔ skipped | |
| `additionalMetadata` | ⛔ skipped | |

**Column Add `ValueJson` fields:** `Name`, `Expression` (object with `Code` or raw string), `DataType`, `Description`.

---

## Unsupported Entities

The following entities produce a **collected error** (not a throw) and are skipped:

| Entity | Reason |
|--------|--------|
| `Relationship` | Reports have no semantic-model relationships in OpenBI (`Relationships` is always empty on PBIR read). |
| `Filter` | Page-level and visual-level filters are not stored or patched in PBIR JSON by this converter. |
| `RefreshTask` | Not stored in PBIR artifact content. |
| `RefreshTrigger` | Same as `RefreshTask`. |
| `DataSourceConnection` | Dataset binding is in `definition.pbir` (`datasetReference`), preserved unchanged from the original artifact. |

---

## Error Handling Behaviour

- Errors are **collected per change**, not thrown. The returned `OpenBIPatchResult.Artifact` reflects all
  successfully applied changes even when some fail.
- `OpenBIPatchResult.IsSuccess` is `false` when any error is collected.
- The MCP server (`upload_asset`) will **refuse to upload** if `IsSuccess` is `false` — it returns the error
  list to the LLM for inspection instead.
- Unexpected exceptions inside a single change are caught and recorded as patch errors; processing continues
  with subsequent changes.
- Replace parts that are skipped (unsupported property names) fail **silently** — no error is recorded for
  them.

---

## Known Limitations

1. **Legacy report ZIPs** cannot be patched. Re-export as PBIR first.

2. **Implicit / aggregated projections** (measures represented only by `Expression`, with
   `IdColumnReference` = null on read) cannot be added or replaced via patch. Workaround: full re-convert via
   `FromOpenBIToArtifactAsync`, or patch at the visual level in Power BI Desktop.

3. **VisualProjection `isMeasure` on Replace** cannot be toggled. Changing Measure ↔ Column requires Remove +
   Add in the same batch.

4. **Page Add without `pages.json`:** the page folder is created, but `pageOrder` is not updated. The page
   still loads (all page directories are scanned), but ordering falls back to directory-name sort until
   `pageOrder` is edited manually or via a subsequent Replace on `order` / another Page Add on an artifact
   that already has `pages.json`.

5. **Table Add does not seed measures:** columns/measures on a new table must be added as separate Column Add
   changes (or use full conversion).

6. **Report extension file optional:** Remove on Table/Column when `reportExtensions.json` is missing succeeds
   silently with no effect. Add/Replace on Column when the file or parent entity is missing collects an error.

7. **No DAX validation:** report extension measure expressions are written as-is. Invalid DAX may upload but
   fail in Power BI.

8. **Bookmarks, themes, static resources** are never modified by patch — only the JSON targets listed above.

9. **Visual Remove is entry-level:** only `visual.json` is removed from the ZIP map; orphaned sibling files
   under the visual folder (if any) are kept.

10. **Round-trip stability:** `VisualProjectionKey` uses `ProjectionName` + 1-based `Order`. Reordering
    projections changes keys; diff → patch → diff is stable when both sides were produced by this converter's
    read path.
