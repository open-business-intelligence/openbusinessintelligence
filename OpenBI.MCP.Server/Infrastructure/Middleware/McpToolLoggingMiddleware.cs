using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenBI.MCP.Server.Infrastructure.Middleware;

public sealed class McpToolLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpToolLoggingMiddleware> _logger;

    public McpToolLoggingMiddleware(RequestDelegate next, ILogger<McpToolLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || !context.Request.Path.StartsWithSegments("/mcp"))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();

        string body;
        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        string? toolName = null;
        string? method = null;
        string? arguments = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("method", out var methodProp))
                method = methodProp.GetString();

            if (root.TryGetProperty("params", out var paramsProp))
            {
                if (paramsProp.TryGetProperty("name", out var nameProp))
                    toolName = nameProp.GetString();
                if (paramsProp.TryGetProperty("arguments", out var argsProp))
                    arguments = argsProp.ToString();
            }
        }
        catch { /* non-JSON or non-tool request — pass through silently */ }

        if (method == "tools/call" && toolName is not null)
            _logger.LogInformation("MCP tool call: {ToolName} args={Arguments}", toolName, arguments);

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            if (method == "tools/call" && toolName is not null)
                _logger.LogInformation(
                    "MCP tool done: {ToolName} status={StatusCode} elapsed={ElapsedMs}ms",
                    toolName, context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }
}
