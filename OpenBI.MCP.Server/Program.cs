using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.Interfaces.Infrastructure;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Application.Platforms;
using OpenBI.MCP.Server.Application.Plugins;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Extensions;
using OpenBI.Common.Compression;
using OpenBI.Patching;
using OpenBI.MCP.Server.Infrastructure.Middleware;
using Serilog;
using Serilog.Extensions.Logging;
using OpenBI.MCP.Server.Infrastructure.Secrets;

var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddJsonFile("secrets.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(Log.Logger);

// â”€â”€ Plugin infrastructure â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// PluginTypeRegistry is populated by PluginLoader before any DI resolution.
var pluginRegistry = new PluginTypeRegistry();
using (var logFactory = new SerilogLoggerFactory(Log.Logger))
{
    var pluginLoader = new PluginLoader(pluginRegistry, logFactory.CreateLogger<PluginLoader>());
    pluginLoader.LoadAll(Path.Combine(AppContext.BaseDirectory, "plugins"));
}
builder.Services.AddSingleton(pluginRegistry);

builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SessionArtifactStore>();
builder.Services.AddSingleton<IArtifactCompressionService, GZipArtifactCompressionService>();
builder.Services.AddSingleton<IOpenBIAssetComparer, OpenBIAssetComparer>();

builder.Services.Configure<SecretsVaultOptions>(configuration.GetSection(SecretsVaultOptions.SectionName));
builder.Services.AddConfiguredSecretsVault();

builder.Services.AddSingleton(sp =>
    BiPlatformRegistry.Load(AppContext.BaseDirectory, sp.GetRequiredService<ILogger<BiPlatformRegistry>>()));

builder.Services.AddSingleton<SiteConnectionFactoryActivator>();
builder.Services.AddSingleton<SiteConverterFactoryActivator>();
builder.Services.AddScoped<SiteConnectionSession>();
builder.Services.AddScoped<ICurrentSessionManager, CurrentSessionManager>();
builder.Services.Configure<SiteRegistryOptions>(configuration.GetSection(SiteRegistryOptions.SectionName));
builder.Services.AddConfiguredSiteRegistry();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

var app = builder.Build();

// Eagerly resolve singletons to catch configuration errors at startup
_ = app.Services.GetRequiredService<ISiteRegistry>();
_ = app.Services.GetRequiredService<BiPlatformRegistry>();

app.UseSerilogRequestLogging();
app.UseMiddleware<McpToolLoggingMiddleware>();

app.MapMcp();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
