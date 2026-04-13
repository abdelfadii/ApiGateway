using System.Diagnostics;

namespace SirmarocGateway.Middleware;

/// <summary>
/// Logs every request/response with method, path, status code, and duration.
/// Skips health-check probes to keep logs clean.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    // Paths we deliberately exclude from logs (noisy, low-value)
    private static readonly string[] _excludedPaths = ["/health", "/favicon.ico"];

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsExcluded(path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;
        var method = context.Request.Method;
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "→ [{RequestId}] {Method} {Path} from {ClientIp}",
            requestId, method, path, clientIp);

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var status = context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error
                       : status >= 400 ? LogLevel.Warning
                       : LogLevel.Information;

            _logger.Log(level,
                "← [{RequestId}] {Method} {Path} → {StatusCode} in {ElapsedMs}ms",
                requestId, method, path, status, sw.ElapsedMilliseconds);
        }
    }

    private static bool IsExcluded(string path) =>
        _excludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}