# Patch tests — Power BI scenario catalog

What we should test when patching **report** and **semantic model** artifacts, using Power BI / Fabric language.

**Implementers:** each scenario maps to `FromOpenBIPatchArtifactAsync` on  
`PowerBIReportPbirOpenBIConverter` (reports) or `PowerBISemanticModelOpenBIConverter` (models).  
OpenBI entity names are in the appendix at the end.

---

## How to read the tables

| Symbol | Meaning for the user / caller |
|--------|-----------------------------|
| ✅ | Change applies; patched file can be saved (`IsSuccess` = true if nothing else failed) |
| ⚠️ | Change skipped; patch continues; warning returned (still `IsSuccess` = true) |
| ❌ | Change rejected; error returned (`IsSuccess` = false); other changes in the same batch may still apply |
| 🚫 | Not supported via patch (ignored or never sent) |
| 💥 | Whole patch aborts with an exception (no result object) |

**Batch rule:** several changes in one call are applied in order. One failure does not stop the rest.

---

# REPORTS (PBIR — Fabric report definition)

**Artifact:** report ZIP with `definition.pbir` (modern PBIR layout).  
**Not patchable:** old PBIX / `report.json`-only layout → must re-export or full convert first.

---

## R0 — Report prerequisites

| # | Scenario | Result | Notes |
|---|----------|--------|-------|
| R0.1 | Patch a valid PBIR report | ✅ | Baseline: at least one page exists |
| R0.2 | Patch a legacy report (no PBIR) | 💥 | “Patch only supported for PBIR” |
| R0.3 | Several changes in one request (e.g. rename page + move visual) | ✅ | All supported steps apply |

---

## R1 — Pages

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| R1.1 | **Add page** | ✅ | New page folder + `page.json`; added to page list if `pages.json` exists |
| R1.2 | Add page without required payload | ❌ | Missing page definition JSON |
| R1.3 | **Delete page** | ✅ | Page folder removed; page order updated; active page fixed if needed |
| R1.4 | Delete page that does not exist | ⚠️ | “Page not found” — no-op |
| R1.5 | Delete page without specifying which page | ❌ | Page id required |
| R1.6 | **Rename page** (display name in UI) | ✅ | Updates `displayName` in page definition |
| R1.7 | Rename page that does not exist | ⚠️ | Skipped |
| R1.8 | **Change page size** (width / height) | ✅ | Canvas size in page definition |
| R1.9 | **Show / hide page in View mode** (Hide page) | ✅ | Hidden → `HiddenInViewMode`; visible → visibility flag removed |
| R1.10 | **Reorder pages** (tab order) | ✅ | Order in `pages.json` page list (clamped to valid range) |
| R1.11 | Change page description, embed URL, extra metadata | 🚫 | Not stored in PBIR page JSON — patch ignores |
| R1.12 | Add page when `pages.json` is missing | ✅* | Page is created; tab order may not update until file exists (*known gap) |

---

## R2 — Report name (workspace display name)

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| R2.1 | **Rename report** (display name) | ✅ | `.platform` → metadata display name |
| R2.2 | Rename when `.platform` file missing | ⚠️ | Skipped |
| R2.3 | “Create report” / “Delete report” via patch | ❌ | Only rename is supported |
| R2.4 | Change description, folder, dataset binding via patch | 🚫 | Not written by patch |

---

## R3 — Visuals on a page

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| R3.1 | **Add visual** to a page | ✅ | New `visual.json` under that page |
| R3.2 | Add visual to missing page | ⚠️ | Page not found |
| R3.3 | Add visual without page id or definition | ❌ | Parent page + payload required |
| R3.4 | **Delete visual** | ✅ | Visual definition removed from ZIP |
| R3.5 | Delete visual that does not exist | ⚠️ | Not found |
| R3.6 | **Rename visual** (name in selection pane) | ✅ | |
| R3.7 | **Change visual type** (e.g. bar → line) | ✅ | `visualType` in definition |
| R3.8 | **Move visual** (X, Y, Z) | ✅ | Position block |
| R3.9 | **Resize visual** (width, height) | ✅ | Position block |
| R3.10 | Add visual **with fields already in wells** (in one step) | ✅ | If payload includes field wells → `queryState` built on add |
| R3.11 | Change category, description, internal OpenBI labels | 🚫 | Not stored separately in PBIR |

---

## R4 — Fields on a visual (wells / query roles)

Power BI: **Category**, **Values**, **Legend**, etc. — stored as `queryState` buckets on the visual.

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| R4.1 | **Add field to visual** (new binding in a well) | ✅ | Needs visual id + field reference + well name |
| R4.2 | Add field without column reference or well name | ❌ | Required fields missing |
| R4.3 | Add field to missing visual | ⚠️ | Visual not found |
| R4.4 | **Remove field from visual** (specific slot in a well) | ✅ | By visual + well + position (1-based index) |
| R4.5 | Remove from wrong index / empty well | ⚠️ | Out of range or bucket missing |
| R4.6 | Remove with invalid field key format | ❌ | Key must encode visual + well + order |
| R4.7 | **Change which column** is in a slot | ✅ | Updates query ref / field definition |
| R4.8 | **Set field as active** (highlight / active selection) | ✅ | Sets or clears `active` flag |
| R4.9 | **Reorder fields** within the same well | ✅ | |
| R4.10 | **Move field to another well** (e.g. Axis → Values) | ✅ | Moves between `queryState` buckets |
| R4.11 | Toggle measure vs column on replace | 🚫 | Use remove + add; replace keeps existing measure/column kind |
| R4.12 | Add implicit / expression-only measure (no column) | 🚫 | Needs `Entity.Column` reference |

---

## R5 — Report-level measures (report extension DAX)

These are **DAX measures stored in the report**, not in the semantic model (Power BI: *report measures* / extension entities).

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| R5.1 | **Add measure group** (extension table / entity) | ✅ | Creates entry in `reportExtensions.json` |
| R5.2 | **Delete measure group** | ✅ | |
| R5.3 | Delete group when file missing | ⚠️ | Skipped |
| R5.4 | Delete group that does not exist | ✅* | Silent no-op (*no warning) |
| R5.5 | **Rename measure group** | ✅ | |
| R5.6 | **Add report measure** under a group | ✅ | Needs parent group name |
| R5.7 | Add measure when group or file missing | ⚠️ | |
| R5.8 | **Delete report measure** | ✅ | |
| R5.9 | Delete measure that does not exist | ✅* | Silent no-op |
| R5.10 | **Rename report measure** | ✅ | |
| R5.11 | **Change measure DAX expression** | ✅ | |
| R5.12 | **Change measure data type / description** | ✅ | |
| R5.13 | Invalid expression JSON on update | ❌ | Parse error |
| R5.14 | Format string, summarize-by, key flags on report measure | 🚫 | Not in report extension file |

---

## R6 — Report: not supported via patch

| Area | Examples | Result |
|------|----------|--------|
| Model relationships | Between tables | 🚫 → ⚠️ if sent |
| Report / page / visual filters | Filter pane | 🚫 |
| Scheduled refresh | Gateway refresh | 🚫 |
| Data source / dataset binding | `definition.pbir` dataset ref | 🚫 (preserved from original) |
| Bookmarks, themes, static resources | | 🚫 (unchanged) |

---

# SEMANTIC MODEL (TMDL — Fabric semantic model)

**Artifact:** model ZIP with `definition/` folder (TMDL).  
**Not patchable:** `model.bim`-only package → full convert required.

**Test setup:** converter needs compression service configured.

---

## M0 — Model prerequisites

| # | Scenario | Result | Notes |
|---|----------|--------|-------|
| M0.1 | Patch valid TMDL model | ✅ | |
| M0.2 | Patch BIM-only model | 💥 | TMDL required |
| M0.3 | Patch corrupt / empty definition | 💥 | Cannot load model |
| M0.4 | Try to patch report-only concepts (pages, visuals) | ⚠️ | Skipped per change |

---

## M1 — Tables

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| M1.1 | **Add table** | ✅ | Import table with M partition; columns/measures in payload added |
| M1.2 | Add table without definition | ❌ | Payload required |
| M1.3 | **Delete table** | ✅ | Table removed (relationships **not** auto-cleaned) |
| M1.4 | Delete table that does not exist | ⚠️ | Skipped |
| M1.5 | **Rename table** | ✅ | |
| M1.6 | Rename table that does not exist | ⚠️ | Skipped |
| M1.7 | **Change Power Query (M) code** on import table | ✅ | First partition M expression updated |
| M1.8 | **Change DAX** on calculated table | ✅ | Calculated partition expression updated |
| M1.9 | Change M code on table with no partition | ✅* | No change, no error (*silent) |
| M1.10 | Change table “type” or extra OpenBI metadata | ⚠️ | Property skipped with warning |

---

## M2 — Columns (regular & calculated)

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| M2.1 | **Add column** to table | ✅ | Regular, calculated, or measure via type in payload |
| M2.2 | Add column to missing table | ⚠️ | Parent table not found |
| M2.3 | **Delete column** | ✅ | |
| M2.4 | Delete column that does not exist | ⚠️ | |
| M2.5 | **Rename column** | ✅ | |
| M2.6 | **Change data type** | ✅ | Regular / calculated columns |
| M2.7 | **Change format string** | ✅ | |
| M2.8 | **Change data category** (e.g. City, Web URL) | ✅ | |
| M2.9 | **Change summarize by** (Sum, Count, None, …) | ✅ | Unrecognized value → no change |
| M2.10 | **Mark as key / nullable / unique** | ✅ | |
| M2.11 | **Change calculated column DAX** | ✅ | Calculated columns only |
| M2.12 | Change DAX on regular imported column | 🚫 | Ignored (no error) |
| M2.13 | **Turn regular column into calculated** (or vice versa) | 🚫 | Remove + add in same batch |
| M2.14 | Change description, OpenBI-only flags | ⚠️ | Warning — property skipped |

---

## M3 — Measures

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| M3.1 | **Add measure** | ✅ | |
| M3.2 | **Delete measure** | ✅ | |
| M3.3 | Delete measure that does not exist | ⚠️ | |
| M3.4 | **Rename measure** | ✅ | |
| M3.5 | **Change measure DAX formula** | ✅ | |
| M3.6 | **Change measure format string** | ✅ | |
| M3.7 | Force measure data type | 🚫 | Engine-derived — warning if attempted |
| M3.8 | Unsupported property on measure | ⚠️ | e.g. description |

---

## M4 — Relationships

| # | Scenario | Result | When it fails |
|---|----------|--------|---------------|
| M4.1 | **Create relationship** between two existing columns | ✅ | Both columns must exist (not measures) |
| M4.2 | Create relationship with missing column | ⚠️ | Relationship not added |
| M4.3 | **Delete relationship** | ✅ | By relationship name |
| M4.4 | Delete relationship that does not exist | ⚠️ | |
| M4.5 | **Replace relationship** (change endpoints, cardinality, name) | ✅ | Full new relationship payload required (remove + add internally) |
| M4.6 | Replace without full payload | ❌ | |
| M4.7 | Change relationship in same batch **after** adding a new column | ✅ | Add column change must come **before** relationship change |

---

## M5 — Semantic model: not supported via patch

| Area | Result |
|------|--------|
| Rename semantic model / workspace display name | 🚫 |
| Report pages, visuals, filters | 🚫 → ⚠️ |
| Refresh schedule, gateway connections | 🚫 |
| `connections.json` / data source entries | 🚫 (preserved) |

---

## M6 — Real-world combinations (integration-style)

| # | Scenario | Expected |
|---|----------|----------|
| M6.1 | Add table → add columns → add relationship | ✅ All in one patch if ordered correctly |
| M6.2 | Rename table + rename column on that table | ✅ |
| M6.3 | Delete table that still has relationships | ⚠️/❌ downstream | Orphan relationships may break serialize/upload |
| M6.4 | Invalid DAX in measure | ✅ patch succeeds | Power BI may fail at refresh — no DAX validation in patch |

---

# Suggested test implementation order

1. **Reports:** R0 → R1 (pages) → R3 (visuals) → R4 (fields) → R2, R5  
2. **Model:** M0 → M1 (tables + PQ) → M3 (measures) → M2 (columns) → M4 (relationships) → M6  

Use minimal Fabric-like ZIP fixtures (one page, one bar chart, one import table with two columns).

---

# Appendix — OpenBI mapping (for automation)

| Power BI concept | OpenBI `Entity` | Operation | Property / notes |
|------------------|-----------------|-----------|------------------|
| Report display name | `AssetInfo` | Replace | `name` |
| Page | `Page` | Add / Remove / Replace | `name`, `width`, `height`, `isEnabled`, `order` |
| Visual | `Visual` | Add / Remove / Replace | `name`, `type`, `x`–`z`, `width`, `height`, `additionalMetadata` |
| Field on visual | `VisualProjection` | Add / Remove / Replace | Key: `{visualId}::{well}::{order}`; `idColumnReference`, `isActive`, `order`, `openBIProjectionName` |
| Report measure group | `Table` (extension) | Add / Remove / Replace | `name` |
| Report measure | `Column` (extension) | Add / Remove / Replace | `name`, `expression`, `dataType`, `description` |
| Model table | `Table` | Add / Remove / Replace | `name`, `expression` (M or calc table DAX) |
| Model column | `Column` | Add / Remove / Replace | See `SemanticModelPatchSupport.md` |
| Model relationship | `Relationship` | Add / Remove / Replace | Replace = full `ValueJson` |

**Outcome detail (technical):** see git history or ask for `PatchArtifact-TestSuite-Design-Technical.md` if you need per-message IDs (`PBIR-ERR-*`, `SM-WARN-*`).

**Capability reference:** `ReportPbirPatchSupport.md`, `SemanticModelPatchSupport.md`.
