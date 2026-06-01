# BI platform packages (`platforms/`)

Each **immediate subdirectory** of `platforms/` is one BI platform package. Files placed directly under `platforms/` (not in a subfolder) are ignored.

## Required files per platform folder

| File | Purpose |
|------|---------|
| `info.json` | Platform identity |
| `visualtypes.json` | Catalog of native visual / projection metadata |

Optional:

| Path | Purpose |
|------|---------|
| `assetTypes/*.md` | One Markdown file per **asset type id** (filename stem) with OpenBI compilation instructions for that kind |

### `assetTypes/` (optional)

- **Discovery:** non-recursive `*.md` files directly under `assetTypes/`.
- **Asset type id:** the **filename without extension** (e.g. `Report.md` → id `Report`). Ids are **platform-defined**; they may align with OpenBI concepts (e.g. `Report`) where helpful, but do not have to match the small OpenBI `AssetType` enum one-to-one (e.g. Power BI may use `SemanticModel`, `Dataflow`).
- **List metadata:** the server exposes each type’s id and, when present, the first Markdown **H1** line (`# ...`) as a short **title** for `list_bi_platform_asset_types`.
- **Content:** full text is returned by `get_bi_platform_asset_type_instructions` (UTF-8).
- If `assetTypes/` is missing or empty, listing returns an empty array.

Invalid file stems (path characters, `..`, wildcards) are skipped with a warning; duplicate stems differing only by case keep the first file and log a warning.

### `info.json`

```json
{ "id": "powerbi", "name": "Microsoft Power BI" }
```

- **`id`** (string, required): Stable identifier; also used as the dictionary key in the server registry (trimmed).
- **`name`** (string, required): Human-readable name.

Property names are camelCase; deserialization is case-insensitive.

### `visualtypes.json`

Supported root shapes:

1. **Recommended:** a JSON **array** of visual type objects.
2. **Alternative:** `{ "visualTypes": [ ... ] }`.

Each element:

| Field | Type | Notes |
|-------|------|--------|
| `objectType` | string | One of: `chart`, `interaction`, `static`, `container` (camelCase enum) |
| `visualType` | string | Platform-specific visual type key |
| `visualDescription` | string | Optional |
| `visualTypeProjection` | string | |
| `projectionDescription` | string | |
| `order` | number | Integer sort order |
| `projectionAllowsMultipleValues` | boolean | |

If `info.json` or `visualtypes.json` is missing or invalid, that platform folder is skipped (with a log entry).

## Correlation with sites

In `sites/*.json`, **`idPlatform`** on a registered site should **match** the platform folder’s `info.json` **`id`** when you want agents and tools to correlate site configuration with this catalog. That is a **convention**; the server does not enforce it unless validation is added later.

## Folder name vs `id`

If the folder name differs from `info.json` `id`, the server logs an informational message but still registers the platform under **`id`**.
