# Security policy

## Reporting a vulnerability

Please report security issues privately to the maintainers via [openbusinessintelligence.net](https://openbusinessintelligence.net) contact channels. Do not open public GitHub issues for undisclosed vulnerabilities.

## Secrets and credentials

- Never commit real credentials under `OpenBI.MCP.Server/secrets/` or any `*.local.json` file.
- Only commit `*.example.json` templates with placeholder values.
- Site JSON files reference secret paths; keep those paths outside version control when they contain production values.

## Supported versions

| Version | Supported |
| ------- | --------- |
| 0.1.x   | Yes       |
