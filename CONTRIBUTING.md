# Contributing to OpenBI

Thank you for helping improve OpenBI. This project follows the [Apache License 2.0](LICENSE).

## Getting started

1. Fork and clone the repository.
2. Install .NET SDK 9.0 (`global.json` pins the minimum version).
3. Build and test:
   ```bash
   dotnet build OpenBI.slnx
   dotnet test OpenBI.slnx
   ```

## Pull requests

- Keep changes focused; one logical change per PR when possible.
- Update documentation when you change public behavior (README, MCP `sites/` docs, platform metadata).
- Do not commit credentials or real `secrets/*.json` files (only `*.example.json` templates).
- Ensure CI passes (`dotnet build` and `dotnet test`).

## Adding a platform

1. Create `OpenBI.Connectors.<Platform>` implementing `ISiteConnection` and `ISiteConnectionFactory`.
2. Create `OpenBI.Converters.<Platform>` implementing `IOpenBIConverter` and `IOpenBIConverterFactory`.
3. Add platform metadata under `OpenBI.MCP.Server/platforms/<platformId>/`.
4. Add a sample site JSON under `OpenBI.MCP.Server/sites/`.
5. Add tests for core model behavior; platform integration tests are welcome but not required for every PR.

## Code style

- C# with nullable reference types enabled.
- Match existing naming and project structure.
- Prefer English for public API documentation (XML comments).

## Questions

Open a GitHub discussion or issue for design questions before large refactors.
