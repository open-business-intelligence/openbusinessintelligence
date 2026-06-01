# MCP Server sites

Place one JSON file per site in this folder (next to the executable after deployment). Files are loaded at startup.

**Secrets vault:** the file-based `ISecretsVaultRepository` is configured in `appsettings.json` under `SecretsVault` — see `ImplementationType` (assembly-qualified name) and `BaseDirectory`.

## Fields

| Field | Description |
|-------|-------------|
| `idSite` | Unique id (string). Use this as `id_site` in `create_session`. |
| `siteName` | Display name. |
| `idPlatform` | Platform id (string). |
| `platformName` | Platform display name. |
| `platformSecretsPath` | Path to a JSON file containing connection key/value pairs (see below). Relative paths are resolved from the application base directory. |
| `siteConnectionFactoryName` | Assembly-qualified factory type, e.g. `OpenBI.Connectors.PowerBI.PowerBISiteConnectionFactory, OpenBI.Connectors.PowerBI`. The MCP server keeps **one singleton** `ISiteConnectionFactory` instance per distinct value (trimmed); use the same spelling everywhere. |
| `siteConverterFactoryName` | Reserved for future use; optional. |

## Secrets JSON (Power BI example)

The root object must be a flat map of string keys to string (or primitive) values. For Power BI, use the keys expected by `PowerBISiteConnection.SetConnectionParameters`:

- `_mspbi_tenantID`
- `_mspbi_clientID`
- `_mspbi_clientSecret`
- `WorkspacesRegex` (optional)

See `sample.site.json.example` and a sample secrets file under `secrets/` if present.
