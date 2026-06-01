# Build context: repo root
#
# Build:
#   docker build -t openbi-mcp-server .
#
# Or with docker compose (if docker-compose.yml is present):
#   docker compose up

# ---------------------------------------------------------------------------- #
# Build stage                                                                   #
# ---------------------------------------------------------------------------- #
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

# Central Package Management
COPY Directory.Packages.props .

# Project files for layer-cached restore
COPY OpenBI/OpenBI.csproj \
     OpenBI/
COPY OpenBI.Interfaces/OpenBI.Interfaces.csproj \
     OpenBI.Interfaces/
COPY OpenBI.Common/OpenBI.Common.csproj \
     OpenBI.Common/
COPY OpenBI.Connectors/OpenBI.Connectors.Interfaces/OpenBI.Connectors.Interfaces.csproj \
     OpenBI.Connectors/OpenBI.Connectors.Interfaces/
COPY OpenBI.Connectors/OpenBI.Connectors.MicrosoftPowerBI/OpenBI.Connectors.PowerBI.csproj \
     OpenBI.Connectors/OpenBI.Connectors.MicrosoftPowerBI/
COPY OpenBI.Converters/OpenBI.Converters.Interfaces/OpenBI.Converters.Interfaces.csproj \
     OpenBI.Converters/OpenBI.Converters.Interfaces/
COPY OpenBI.Converters/OpenBI.Converters.PowerBI/OpenBI.Converters.PowerBI.csproj \
     OpenBI.Converters/OpenBI.Converters.PowerBI/
COPY OpenBI.MCP.Server/OpenBI.MCP.Server.csproj \
     OpenBI.MCP.Server/

RUN dotnet restore OpenBI.MCP.Server/OpenBI.MCP.Server.csproj

# Copy source
COPY OpenBI/                                               OpenBI/
COPY OpenBI.Interfaces/                                    OpenBI.Interfaces/
COPY OpenBI.Common/                                        OpenBI.Common/
COPY OpenBI.Connectors/OpenBI.Connectors.Interfaces/       OpenBI.Connectors/OpenBI.Connectors.Interfaces/
COPY OpenBI.Connectors/OpenBI.Connectors.MicrosoftPowerBI/ OpenBI.Connectors/OpenBI.Connectors.MicrosoftPowerBI/
COPY OpenBI.Converters/OpenBI.Converters.Interfaces/       OpenBI.Converters/OpenBI.Converters.Interfaces/
COPY OpenBI.Converters/OpenBI.Converters.PowerBI/          OpenBI.Converters/OpenBI.Converters.PowerBI/
COPY OpenBI.MCP.Server/                                    OpenBI.MCP.Server/

RUN dotnet publish OpenBI.MCP.Server/OpenBI.MCP.Server.csproj \
    -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------- #
# Runtime stage                                                                 #
# ---------------------------------------------------------------------------- #
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Mount these volumes to provide external configuration at runtime:
#   sites/     <- site JSON files
#   plugins/   <- connector / converter plugin folders
#   platforms/ <- platform info, visual types, asset type instructions
#   secrets/   <- site credential files (do not bake into image)
VOLUME ["/app/sites", "/app/plugins", "/app/platforms", "/app/secrets"]

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OpenBI.MCP.Server.dll"]
