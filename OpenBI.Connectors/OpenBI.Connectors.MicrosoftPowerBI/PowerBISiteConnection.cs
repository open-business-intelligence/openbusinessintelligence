using OpenBI.Interfaces.Infrastructure;
using OpenBI.Connectors.Interfaces;
using OpenBI.Connectors.Interfaces.Models;
using OpenBI.Connectors.PowerBI.Http;
using OpenBI.Connectors.PowerBI.Models.Fabric;
using Microsoft.Extensions.Logging;
using Microsoft.Fabric.Api.Utils;
using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Microsoft.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBI.Connectors.PowerBI
{
    public class PowerBISiteConnection : ISiteConnection
    {
        protected readonly ILogger<PowerBISiteConnection> _logger;
        protected IArtifactCompressionService? _compression;

        protected string? _tenantId;
        protected string? _clientId;
        protected string? _clientSecret;
        protected string? _workspacesRegex; // Optional filter
        protected bool _interactive;        // true → delegated-user auth via PowerShell

        // Static interactive-token cache: keyed by OS username so it survives instance recreation
        // (e.g. scoped/transient DI in an MCP-server scenario).
        // Holds both the Fabric-scoped and PowerBI-scoped tokens from the same auth session.
        private sealed record InteractiveTokenPair(string FabricToken, string PowerBIToken, DateTime ExpiresAt);
        private static readonly ConcurrentDictionary<string, InteractiveTokenPair> _interactiveTokenCache = new();
        private static readonly SemaphoreSlim _interactiveTokenLock = new(1, 1);

        public PowerBISiteConnection(ILogger<PowerBISiteConnection> logger, IArtifactCompressionService? compression = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _compression = compression;
        }

        public virtual void SetConnectionParameters(IDictionary<string, string> parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            // Determine auth mode first
            parameters.TryGetValue("_interactive", out var interactiveStr);
            _interactive = "true".Equals(interactiveStr, StringComparison.OrdinalIgnoreCase);

            if (_interactive)
            {
                // Delegated-user auth: only a client ID is required (no secret).
                // Use a synthetic tenant key for cache isolation (actual tenant is resolved
                // from the interactive login, not from parameters).
                if (!parameters.TryGetValue("_mspbi_clientID", out _clientId) || string.IsNullOrWhiteSpace(_clientId))
                    throw new ArgumentException(
                        "ClientId is required for interactive auth. " +
                        "Provide your own app registration, or use the well-known Azure PowerShell client ID: 1950a258-227b-4e31-a9cf-717495945fc2");
                _tenantId = $"__interactive__{Environment.UserName}";
            }
            else
            {
                // Service-principal auth: all three credentials are required.
                if (!parameters.TryGetValue("_mspbi_tenantID", out _tenantId) || string.IsNullOrWhiteSpace(_tenantId))
                    throw new ArgumentException("TenantId is required in connection parameters");

                if (!parameters.TryGetValue("_mspbi_clientID", out _clientId) || string.IsNullOrWhiteSpace(_clientId))
                    throw new ArgumentException("ClientId is required in connection parameters");

                if (!parameters.TryGetValue("_mspbi_clientSecret", out _clientSecret) || string.IsNullOrWhiteSpace(_clientSecret))
                    throw new ArgumentException("ClientSecret is required in connection parameters");
            }

            parameters.TryGetValue("WorkspacesRegex", out _workspacesRegex); // Optional in both modes
        }

        public async Task<ICollection<SiteAsset>> QuerySiteAssetsAsync(QueryAssetsParameters? parameters = null)
        {
            var token = await GetTokenAsync();
            var pbiclient = CreatePowerBIClient(token);
            var fabricClient = GenerateFabricClient(token);

            var assets = new List<SiteAsset>();

            // Get workspaces (filtered by FolderNameFilter or FolderId)
            var workspaces = await GetWorkspacesAsync(fabricClient, parameters?.FolderNameFilter, parameters?.FolderId);

            // Determine asset type from AdditionalParameters
            var assetType = parameters?.AssetType;

            foreach (var workspace in workspaces)
            {
                if (!string.IsNullOrEmpty(assetType))
                {
                    // Query specific asset type
                    assets.AddRange(await QueryAssetsByTypeAsync(pbiclient, workspace, assetType, parameters));
                }
                else
                {
                    // Query all asset types
                    assets.AddRange(await QueryReportsAsync(pbiclient, workspace, parameters));
                    assets.AddRange(await QuerySemanticModelsAsync(pbiclient, workspace, parameters));
                }
            }

            // Apply NameFilter and AssetId filter
            return ApplyFilters(assets, parameters);
        }

        public async Task<Stream> DownloadAssetArtifactAsync(string assetId, string assetType)
        {
            var bytes = await ((ISiteConnection)this).DownloadAssetArtifactAsync(assetId, assetType);
            return new MemoryStream(bytes);
        }

        protected async Task<string> GetPowerBIAccessTokenAsync()
        {
            if (_interactive) return (await GetInteractiveTokenPairAsync()).PowerBIToken;

            var app = ConfidentialClientApplicationBuilder
                .Create(_clientId)
                .WithClientSecret(_clientSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_tenantId}"))
                .Build();
            var result = await app.AcquireTokenForClient(
                new[] { "https://analysis.windows.net/powerbi/api/.default" })
                .ExecuteAsync();
            return result.AccessToken;
        }

        protected async Task<string> GetTokenAsync()
        {
            if (_interactive) return (await GetInteractiveTokenPairAsync()).FabricToken;

            var app = ConfidentialClientApplicationBuilder
                .Create(_clientId)
                .WithClientSecret(_clientSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_tenantId}"))
                .Build();

            var result = await app.AcquireTokenForClient(
                new[] { "https://api.fabric.microsoft.com/.default" })
                .ExecuteAsync();

            return result.AccessToken;
        }

        protected HttpClient GenerateFabricClient(string bearerToken)
        {
            var retryHandler = new OpenBI.Connectors.PowerBI.Http.RetryAfterDelegatingHandler(_logger, maxRetries: 5)
            {
                InnerHandler = new HttpClientHandler()
            };
            var client = new HttpClient(retryHandler)
            {
                BaseAddress = new Uri("https://api.fabric.microsoft.com/v1/")
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
        }

        protected PowerBIClient CreatePowerBIClient(string bearerToken)
        {
            return new PowerBIClient(bearerToken);
        }

        protected dynamic GetReportsInGroupCached(PowerBIClient client, Guid groupId)
        {
            if (PowerBIApiCache.Instance.TryGetReportsInGroup(_tenantId!, groupId, out var cached) && cached != null)
                return cached;
            var response = client.Reports.GetReportsInGroup(groupId);
            PowerBIApiCache.Instance.SetReportsInGroup(_tenantId!, groupId, response);
            return response;
        }

        protected dynamic GetDatasetsInGroupCached(PowerBIClient client, Guid groupId)
        {
            if (PowerBIApiCache.Instance.TryGetDatasetsInGroup(_tenantId!, groupId, out var cached) && cached != null)
                return cached;
            var response = client.Datasets.GetDatasetsInGroup(groupId);
            PowerBIApiCache.Instance.SetDatasetsInGroup(_tenantId!, groupId, response);
            return response;
        }

        protected async Task<T> HttpGet<T>(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"GET {url} returned {(int)response.StatusCode} {response.StatusCode}. Body: {errorBody}");
            }
            var responseBody = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(responseBody)!;
        }

        /// <summary>
        /// Core POST + poll logic shared by both LRO overloads.
        /// Returns (immediateBody, null) when the operation completed synchronously (200/201),
        /// or (null, pollUrl) when a long-running operation succeeded.
        /// Throws <see cref="HttpRequestException"/> on failure or unexpected status.
        /// </summary>
        private async Task<(string? immediateBody, string? pollUrl)> HttpPostAndWaitAsync(HttpClient client, string url, HttpContent body)
        {
            var response = await client.PostAsync(url, body);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                return (responseBody, null);

            if (response.StatusCode != HttpStatusCode.Accepted)
                throw new HttpRequestException($"Unexpected status code: {response.StatusCode}");

            var location = response.Headers.Location;
            var pollUrl = location?.IsAbsoluteUri == true ? location.AbsoluteUri : (client.BaseAddress != null && location != null ? new Uri(client.BaseAddress, location).AbsoluteUri : response.Headers.Location!.AbsolutePath);

            LongRunningOperationStatus? status = null;
            HttpResponseMessage? lroResponse = null;

            string? lastPollBody = null;
            do
            {
                await Task.Delay(1000);
                lroResponse = await client.GetAsync(pollUrl);
                lastPollBody = await lroResponse.Content.ReadAsStringAsync();
                status = JsonConvert.DeserializeObject<LongRunningOperationStatus>(lastPollBody);
            } while (lroResponse.StatusCode == HttpStatusCode.Accepted || string.Equals(status?.status, "Running", StringComparison.OrdinalIgnoreCase));

            if (string.Equals(status?.status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = "Operation failed (no error details).";
                if (status?.error != null)
                {
                    var code = status.error.errorCode;
                    var msg = status.error.message;
                    errMsg = string.IsNullOrWhiteSpace(msg)
                        ? (string.IsNullOrWhiteSpace(code) ? lastPollBody ?? errMsg : code)
                        : (string.IsNullOrWhiteSpace(code) ? msg : $"{code}: {msg}");
                }
                else if (!string.IsNullOrWhiteSpace(lastPollBody))
                    errMsg = lastPollBody;
                throw new HttpRequestException($"Fabric long-running operation failed. {errMsg}");
            }

            if (!string.Equals(status?.status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException($"Unexpected LRO status: {status?.status}. Response: {lastPollBody}");

            return (null, pollUrl);
        }

        /// <summary>
        /// Executes a Fabric long-running POST operation, polls until completion, then fetches and deserializes the /result.
        /// Use when the operation returns a result payload (e.g. create endpoints).
        /// </summary>
        private async Task<T> HttpPostLongOperation<T>(HttpClient client, string url, HttpContent body) where T : class
        {
            var (immediateBody, pollUrl) = await HttpPostAndWaitAsync(client, url, body);
            if (immediateBody != null)
                return JsonConvert.DeserializeObject<T>(immediateBody)!;
            var resultPath = pollUrl!.TrimEnd('/') + "/result";
            return await HttpGet<T>(client, resultPath);
        }

        /// <summary>
        /// Executes a Fabric long-running POST operation, polls until completion, and returns with no result.
        /// Use when the operation has no /result endpoint (e.g. updateDefinition).
        /// Throws on failure; returns normally on success.
        /// </summary>
        private async Task HttpPostLongOperation(HttpClient client, string url, HttpContent body)
        {
            await HttpPostAndWaitAsync(client, url, body);
        }

        private async Task<List<Workspace>> GetWorkspacesAsync(HttpClient fabricClient, string? folderNameFilter, string? folderId)
        {
            var workspacesResponse = await HttpGet<WorkspaceResponse>(fabricClient, "workspaces");
            var workspaces = workspacesResponse.Value
                .Where(x => x.Type != "Personal")
                .ToList();

            // Apply folder name filter
            if (!string.IsNullOrWhiteSpace(folderNameFilter))
            {
                var regex = new Regex(folderNameFilter);
                workspaces = workspaces.Where(w => regex.IsMatch(w.DisplayName)).ToList();
            }

            // Apply folder ID filter
            if (!string.IsNullOrWhiteSpace(folderId) && Guid.TryParse(folderId, out var workspaceId))
            {
                workspaces = workspaces.Where(w => w.Id == workspaceId).ToList();
            }

            // Apply connection-level workspace regex filter
            if (!string.IsNullOrWhiteSpace(_workspacesRegex))
            {
                var regex = new Regex(_workspacesRegex);
                workspaces = workspaces.Where(w => regex.IsMatch(w.DisplayName)).ToList();
            }

            return workspaces;
        }

        private async Task<List<SiteAsset>> QueryReportsAsync(PowerBIClient pbiclient, Workspace workspace, QueryAssetsParameters? parameters)
        {
            var assets = new List<SiteAsset>();
            var reports = GetReportsInGroupCached(pbiclient, workspace.Id);

            if (reports?.Value != null)
            {
                foreach (var report in reports.Value.Value)
                {
                    assets.Add(new SiteAsset
                    {
                        Id = report.Id.ToString(),
                        Name = report.Name,
                        Type = "Report",
                        Path = $"{workspace.DisplayName}/{report.Name}",
                        WebUrl = report.WebUrl,
                        EmbedUrl = report.EmbedUrl,
                    });
                }
            }

            return assets;
        }

        private async Task<List<SiteAsset>> QuerySemanticModelsAsync(PowerBIClient pbiclient, Workspace workspace, QueryAssetsParameters? parameters)
        {
            var assets = new List<SiteAsset>();
            var datasets = GetDatasetsInGroupCached(pbiclient, workspace.Id);

            if (datasets?.Value != null)
            {
                foreach (var dataset in datasets.Value.Value)
                {
                    assets.Add(new SiteAsset
                    {
                        Id = dataset.Id,
                        Name = dataset.Name,
                        Type = "SemanticModel",
                        Path = $"{workspace.DisplayName}/{dataset.Name}",
                        Creator = dataset.ConfiguredBy,
                        WebUrl = dataset.WebUrl,
                        EmbedUrl = dataset.QnaEmbedURL,
                    });
                }
            }

            return assets;
        }

        private async Task<List<SiteAsset>> QueryAssetsByTypeAsync(PowerBIClient pbiclient, Workspace workspace, string assetType, QueryAssetsParameters? parameters)
        {
            return assetType.ToLowerInvariant() switch
            {
                "report" => await QueryReportsAsync(pbiclient, workspace, parameters),
                "semanticmodel" => await QuerySemanticModelsAsync(pbiclient, workspace, parameters),
                _ => new List<SiteAsset>()
            };
        }

        private List<SiteAsset> ApplyFilters(List<SiteAsset> assets, QueryAssetsParameters? parameters)
        {
            if (parameters == null)
                return assets;

            // Apply NameFilter
            if (!string.IsNullOrWhiteSpace(parameters.NameFilter))
            {
                var regex = new Regex(parameters.NameFilter);
                assets = assets.Where(a => regex.IsMatch(a.Name)).ToList();
            }

            // Apply AssetId filter
            if (!string.IsNullOrWhiteSpace(parameters.AssetId))
            {
                assets = assets.Where(a => a.Id == parameters.AssetId).ToList();
            }

            return assets;
        }

        protected async Task<Guid?> FindWorkspaceForAssetAsync(string assetId, string assetType)
        {
            var token = await GetTokenAsync();
            var pbiclient = CreatePowerBIClient(token);
            var fabricClient = GenerateFabricClient(token);

            var workspaces = await HttpGet<WorkspaceResponse>(fabricClient, "workspaces");

            foreach (var workspace in workspaces.Value.Where(x => x.Type != "Personal"))
            {
                // Check reports
                if (assetType.Equals("Report", StringComparison.OrdinalIgnoreCase))
                {
                    var reports = GetReportsInGroupCached(pbiclient, workspace.Id);
                    if (reports?.Value?.Value != null)
                    {
                        foreach (var r in reports.Value.Value)
                            if (r.Id.ToString() == assetId) return workspace.Id;
                    }
                }

                // Check semantic models
                if (assetType.Equals("SemanticModel", StringComparison.OrdinalIgnoreCase))
                {
                    var datasets = GetDatasetsInGroupCached(pbiclient, workspace.Id);
                    if (datasets?.Value?.Value != null)
                    {
                        foreach (var d in datasets.Value.Value)
                            if (d.Id == assetId) return workspace.Id;
                    }
                }

            }

            return null;
        }

        async Task<byte[]> ISiteConnection.DownloadAssetArtifactAsync(string assetId, string assetType)
        {
            // Iterate over all workspaces to find the asset
            var workspaceId = await FindWorkspaceForAssetAsync(assetId, assetType);
            if (!workspaceId.HasValue)
            {
                throw new InvalidOperationException($"Asset {assetId} of type {assetType} not found in any workspace");
            }

            var token = await GetTokenAsync();
            var powerBiClient = CreatePowerBIClient(token);
            var fabricClient = GenerateFabricClient(token);

            bool downloadConnections = false;

            // Determine API endpoint based on asset type
            string endpoint;
            switch (assetType.ToLowerInvariant())
            {
                case "report":
                    endpoint = $"workspaces/{workspaceId}/reports/{assetId}/getDefinition";
                    break;
                case "semanticmodel":
                    endpoint = $"workspaces/{workspaceId}/semanticModels/{assetId}/getDefinition";
                    downloadConnections = true;
                    break;
                default:
                    throw new ArgumentException($"Unsupported asset type: {assetType}");
            }

            // Call Fabric API to get definition (long-running operation)
            var definition = await HttpPostLongOperation<GetItemDefinitionResponse>(
                fabricClient,
                endpoint,
                new StringContent("")
            );

            if (definition == null)
            {
                throw new InvalidOperationException($"Failed to retrieve definition for asset {assetId}");
            }

            if (_compression == null)
            {
                throw new InvalidOperationException("Compression service is required for download. Inject IArtifactCompressionService when creating PowerBISiteConnection.");
            }

            var parts = definition.definition?.parts;
            if (parts == null || parts.Count == 0)
            {
                throw new InvalidOperationException($"Definition for asset {assetId} has no parts.");
            }

            var entries = parts.Select(p =>
            {
                var path = (p.path ?? string.Empty).Replace('\\', '/');
                var content = string.IsNullOrEmpty(p.payload) ? Array.Empty<byte>() : Convert.FromBase64String(p.payload);
                return (path, content);
            }).ToList();

            if (downloadConnections)
            {
                var connections = powerBiClient.Datasets.GetDatasourcesInGroup(workspaceId.Value, assetId);

                if (connections?.Value?.Value != null)
                {
                    var connectionsAsString = System.Text.Json.JsonSerializer.Serialize(connections.Value.Value);

                    var connectionsAsBytes = Encoding.UTF8.GetBytes(connectionsAsString);

                    entries.Add(("connections.json", connectionsAsBytes));
                }                
            }

            var zipBytes = await _compression.ZipEntriesAsync(entries);

            _logger.LogInformation("Downloaded artifact for asset {AssetId} (Type: {AssetType})",
                assetId, assetType);

            return zipBytes;
        }

        public async Task<string> UploadAssetArtifactAsync(string idFolder, string? idAsset, byte[] artifact)
        {
            if (string.IsNullOrWhiteSpace(idFolder))
                throw new ArgumentException("idFolder (workspace ID) is required.", nameof(idFolder));
            if (artifact == null || artifact.Length == 0)
                throw new ArgumentException("artifact is required.", nameof(artifact));
            if (_compression == null)
                throw new InvalidOperationException("Compression service is required for upload. Inject IArtifactCompressionService when creating PowerBISiteConnection.");
            
            using var zip = new ZipArchive(new MemoryStream(artifact), ZipArchiveMode.Read);

            var assetType = InferAssetTypeFromZip(zip);
            var isCreate = string.IsNullOrWhiteSpace(idAsset);

            var createdId = await UploadFabricItemAsync(zip, idFolder, idAsset, assetType, isCreate);

            _logger.LogInformation("Uploaded {AssetType} {Action} in workspace {WorkspaceId}", assetType, isCreate ? "created" : "updated", idFolder);
            return createdId;
        }

        private static string InferAssetTypeFromZip(ZipArchive zip)
        {
            var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            var hasReport = entries.Any(e => e.EndsWith("definition.pbir", StringComparison.OrdinalIgnoreCase) || e.EndsWith("report.json", StringComparison.OrdinalIgnoreCase));
            var hasSemanticModel = entries.Any(e => e.EndsWith("model.bim", StringComparison.OrdinalIgnoreCase) || e.EndsWith("definition.pbism", StringComparison.OrdinalIgnoreCase));

            if (hasReport) return "Report";
            if (hasSemanticModel) return "SemanticModel";

            throw new ArgumentException("Could not infer asset type from zip entries. Expected definition.pbir/report.json (Report) or model.bim/definition.pbism (SemanticModel).");
        }

        private static string GetDisplayNameFromZip(ZipArchive zip, string assetType)
        {
            var platformEntry = zip.Entries.FirstOrDefault(e => e.Name.Equals(".platform", StringComparison.OrdinalIgnoreCase));
            if (platformEntry == null)
                throw new InvalidOperationException("Zip must contain .platform file to get displayName.");
            using var stream = platformEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var platform = JsonConvert.DeserializeObject<PlatformMetadata>(json);
            var displayName = platform?.metadata?.displayName;
            if (string.IsNullOrWhiteSpace(displayName))
                throw new InvalidOperationException(".platform metadata.displayName is required for create.");
            return displayName;
        }

        private static Definition BuildFabricDefinitionFromZip(ZipArchive zip, string? format = null)
        {
            var parts = new List<Part>();

            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            zip.ExtractToDirectory(tempPath);

            foreach (var entry in zip.Entries.Where(e => e.Length > 0))
            {
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var path = entry.FullName.Replace('\\', '/');
                parts.Add(new Part
                {
                    path = path,
                    payload = Convert.ToBase64String(ms.ToArray()),
                    payloadType = "InlineBase64"
                });
            }
            return new Definition { parts = parts, format = format };
        }

        private async Task<string?> UploadFabricItemAsync(ZipArchive zip, string idFolder, string? idAsset, string assetType, bool isCreate)
        {
            var token = await GetTokenAsync();
            var fabricClient = GenerateFabricClient(token);
            var definition = BuildFabricDefinitionFromZip(zip, assetType.ToLowerInvariant() == "semanticmodel" ? "TMDL" : null);

            Guid workspaceId;
            if (isCreate)
            {
                workspaceId = Guid.Parse(idFolder);
            }
            else
            {
                var ws = await FindWorkspaceForAssetAsync(idAsset!, assetType);
                if (!ws.HasValue)
                    throw new InvalidOperationException($"Asset {idAsset} of type {assetType} not found in any workspace.");
                workspaceId = ws.Value;
            }

            var jsonContent = (HttpContent?)null;
            if (isCreate)
            {
                var displayName = GetDisplayNameFromZip(zip, assetType);
                var createPayload = new CreateFabricItemRequest { displayName = displayName, definition = definition };
                jsonContent = new StringContent(JsonConvert.SerializeObject(createPayload), Encoding.UTF8, "application/json");

                var createUrl = assetType.ToLowerInvariant() switch
                {
                    "report" => $"workspaces/{workspaceId}/reports",
                    "semanticmodel" => $"workspaces/{workspaceId}/semanticModels",
                    _ => throw new ArgumentException($"Unsupported Fabric item type: {assetType}")
                };
                var created = await HttpPostLongOperation<FabricItemResponse>(fabricClient, createUrl, jsonContent);
                PowerBIApiCache.Instance.InvalidateWorkspace(_tenantId, workspaceId);
                return created?.id;
            }
            else
            {
                var updatePayload = new UpdateDefinitionRequest { definition = definition };
                jsonContent = new StringContent(JsonConvert.SerializeObject(updatePayload), Encoding.UTF8, "application/json");

                var updateUrl = assetType.ToLowerInvariant() switch
                {
                    "report" => $"workspaces/{workspaceId}/reports/{idAsset}/updateDefinition",
                    "semanticmodel" => $"workspaces/{workspaceId}/semanticModels/{idAsset}/updateDefinition",
                    _ => throw new ArgumentException($"Unsupported Fabric item type: {assetType}")
                };
                await HttpPostLongOperation(fabricClient, updateUrl, jsonContent);
                PowerBIApiCache.Instance.InvalidateWorkspace(_tenantId, workspaceId);
                return null;
            }
        }

        /// <summary>
        /// Returns a cached token pair (Fabric + PowerBI) for the current OS user, acquiring them
        /// interactively via MSAL device code flow when no valid cached pair exists.
        /// At most one auth flow runs at a time; subsequent callers wait and reuse the same pair.
        /// </summary>
        private async Task<InteractiveTokenPair> GetInteractiveTokenPairAsync()
        {
            var cacheKey = Environment.UserName;

            // Fast path — valid pair already cached
            if (_interactiveTokenCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
                return cached;

            // Slow path — serialize so only one auth flow runs at a time
            await _interactiveTokenLock.WaitAsync();
            try
            {
                // Double-check after acquiring the lock
                if (_interactiveTokenCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow < cached.ExpiresAt)
                    return cached;

                // "common" authority allows login from any tenant without knowing the tenant ID upfront.
                var app = PublicClientApplicationBuilder
                    .Create(_clientId!)
                    .WithAuthority("https://login.microsoftonline.com/common/")
                    .Build();

                // Power BI and Fabric auth are unified — one Fabric-scoped token covers both APIs.
                var result = await app
                    .AcquireTokenWithDeviceCode(
                        new[] { "https://api.fabric.microsoft.com/.default" },
                        dc =>
                        {
                            // Print the device code prompt so the user always sees it,
                            // regardless of how the logger is configured.
                            Console.WriteLine(dc.Message);
                            _logger.LogInformation("{Message}", dc.Message);
                            return Task.CompletedTask;
                        })
                    .ExecuteAsync();

                var token     = result.AccessToken;
                var expiresAt = result.ExpiresOn.UtcDateTime.AddMinutes(-2); // 2-minute safety margin
                var pair      = new InteractiveTokenPair(token, token, expiresAt);
                _interactiveTokenCache[cacheKey] = pair;
                _logger.LogInformation("Interactive token acquired, valid until {ExpiresAt:u}.", expiresAt);
                return pair;
            }
            finally
            {
                _interactiveTokenLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SiteFolderInfo>> GetSiteFoldersAsync(CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync().ConfigureAwait(false);
            var fabricClient = GenerateFabricClient(token);
            var workspaces = await HttpGet<WorkspaceResponse>(fabricClient, "workspaces").ConfigureAwait(false);
            var list = new List<SiteFolderInfo>();
            if (workspaces?.Value == null)
                return list;

            foreach (var w in workspaces.Value.Where(x => x.Type != "Personal"))
            {
                if (!string.IsNullOrEmpty(_workspacesRegex) && !Regex.IsMatch(w.DisplayName, _workspacesRegex))
                    continue;
                list.Add(new SiteFolderInfo
                {
                    Id = w.Id.ToString(),
                    Name = w.DisplayName,
                    Type = w.Type,
                    FullPath = w.DisplayName
                });
            }

            return list;
        }

        /// <inheritdoc />
        public async Task<SiteFolderInfo> CreateSiteFolderAsync(CreateSiteFolderRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Name is required.", nameof(request));

            var token = await GetTokenAsync();

            var pbiClient = CreatePowerBIClient(token);

            var group = pbiClient.Groups.CreateGroup(new GroupCreationRequest(request.Name), true);

            return new SiteFolderInfo
            {
                Id = group.Value.Id.ToString(),
                Name = group.Value.Name ?? string.Empty,
                Type = "Workspace",
                FullPath = group.Value.Name ?? string.Empty,
            };
        }

        public void SetCompressionService(IArtifactCompressionService compressionService)
        {
            this._compression = compressionService;
        }
    }
}
