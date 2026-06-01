# PowerBI PBIR Report — Patch Support Reference

Documents what `FromOpenBIPatchArtifactAsync` supports for PBIR (`.Report/definition/` folder-based format).  
Legacy PBIX-based reports (ZIP without `definition.pbir`) are **not supported** and throw `NotSupportedException`.

---

## How it works

Changes are applied directly to the raw ZIP bytes using `JObject` path manipulation — no typed round-trip.  
Unknown JSON properties in any PBIR file are preserved verbatim.  
Errors are collected per-change; the artifact reflects all **successful** changes even when some fail.

---

## Supported Entities

### AssetInfo

| Op      | Supported | Notes |
|---------|-----------|-------|
| Replace | ✅        | Updates `.platform` |

**Replaceable properties:**

| Property | PBIR path |
|----------|-----------|
| `name`   | `.platform["metadata"]["displayName"]` |

---

### Page

| Op      | Supported | Notes |
|---------|-----------|-------|
| Add     | ✅        | Appended to `pages.json` → `pageOrder` |
| Remove  | ✅        | Removes page folder + `pageOrder` entry; resets `activePage` if it was the active page |
| Replace | ✅        | See properties below |

**Replaceable properties:**

| Property   | PBIR path | Notes |
|------------|-----------|-------|
| `name`     | `page.json["displayName"]` | |
| `width`    | `page.json["width"]` | Numeric |
| `height`   | `page.json["height"]` | Numeric |
| `isEnabled`| `page.json["visibility"]` | `false` → `"HiddenInViewMode"`; `true` → removes the field |
| `order`    | `pages.json["pageOrder"]` | Repositions page ID within the ordered array |

**Skipped properties** (not stored in `page.json`):  
`description`, `embedPageUrlParameter`, `embedPageUrl`, `additionalMetadata`

---

### Visual

| Op      | Supported | Notes |
|---------|-----------|-------|
| Add     | ✅        | Requires `ParentId` = page Id |
| Remove  | ✅        | |
| Replace | ✅        | See properties below |

**Replaceable properties:**

| Property           | PBIR path | Notes |
|--------------------|-----------|-------|
| `name`             | `visual.json["name"]` | |
| `type`             | `visual.json["visual"]["visualType"]` | |
| `x`                | `visual.json["position"]["x"]` | Numeric |
| `y`                | `visual.json["position"]["y"]` | Numeric |
| `z`                | `visual.json["position"]["z"]` | Numeric |
| `width`            | `visual.json["position"]["width"]` | Numeric |
| `height`           | `visual.json["position"]["height"]` | Numeric |
| `additionalMetadata` | `visual.json["visual"]["objects"]` and/or `visual.json["visual"]["visualContainerObjects"]` | The part `ValueJson` is a JSON array of `{Name, Value}` items; only `objects` and `visualContainerObjects` keys are written |

**Skipped properties** (not stored separately):  
`category`, `openBIVisualType`, `description`

---

### VisualProjection

Identity key format: **`{visualId}::{projectionName}::{order}`** (1-based order) — encoded by `VisualProjectionKey`.

| Op      | Supported | Notes |
|---------|-----------|-------|
| Add     | ✅        | Requires `ParentId` = visual Id; `IdColumnReference` and `ProjectionName` required in `ValueJson` |
| Remove  | ✅        | |
| Replace | ✅        | See properties below |

**Target path:** `visual.json["visual"]["query"]["queryState"][projectionName]["projections"][order-1]`

**Replaceable properties:**

| Property              | Effect |
|-----------------------|--------|
| `isActive`            | `true` → sets `"active": true`; `false` → removes `"active"` field |
| `idColumnReference`   | Rebuilds `field`, `queryRef`, `nativeQueryRef`; infers Measure vs Column from existing `field` shape |
| `order`               | Moves projection to new 1-based index within the same bucket |
| `openBIProjectionName`| Moves projection to a different bucket key in `queryState` |

**Skipped properties:**  
`isDimension`, `isMeasure`, `expression`, `additionalMetadata`, `type`

---

### Table  *(ReportExtension measures)*

Stored in `definition/reportExtensions.json` → `entities[]`.  
Identity key: entity `name`.

| Op      | Supported | Notes |
|---------|-----------|-------|
| Add     | ✅        | Creates entity with empty `measures` array; creates `reportExtensions.json` if absent |
| Remove  | ✅        | |
| Replace | ✅        | `name` only |

---

### Column  *(ReportExtension measure)*

Stored as a measure inside an entity in `reportExtensions.json`.  
Identity key: measure `name`. Parent key: table `name` (via `ParentId` for Add).

| Op      | Supported | Notes |
|---------|-----------|-------|
| Add     | ✅        | Requires `ParentId` = table name |
| Remove  | ✅        | Searches all entities |
| Replace | ✅        | See properties below |

**Replaceable properties:**

| Property     | Notes |
|--------------|-------|
| `name`       | |
| `expression` | Reads `Expression.Code` from the part value; written as `measure["expression"]` |
| `dataType`   | Maps `Column.ColumnDataType` int → `PrimitiveTypeName` string |
| `description`| |

---

## Unsupported Entities

The following entities produce one `OpenBIPatchError` per change; the artifact still reflects all other successful changes.

| Entity               |
|----------------------|
| `Filter`             |
| `Relationship`       |
| `RefreshTask`        |
| `RefreshTrigger`     |
| `DataSourceConnection` |

---

## ID Stability Note

`MapReportExtensionsToOpenBiTables` sets `Id = entity.Name` on `Table` and `Id = measure.Name` on `Column` so that `OpenBIAssetComparer` can emit patch changes for ReportExtension measures. Without stable IDs the comparer silently drops those entities.
