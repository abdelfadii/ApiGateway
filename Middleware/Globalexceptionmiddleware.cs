using System.Net;
using System.Text.Json;

namespace SirmarocGateway.Middleware;

/// <summary>
/// Catches any unhandled exception in the pipeline and returns a
/// structured JSON error response — never leaks stack traces to clients.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception on {Method} {Path} — TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            await WriteErrorResponseAsync(context, ex);
        }
    }

    private async Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = ex switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized."),
            ArgumentException => (HttpStatusCode.BadRequest, "Bad request."),
            TimeoutException => (HttpStatusCode.GatewayTimeout, "Upstream service timed out."),
            HttpRequestException => (HttpStatusCode.BadGateway, "Upstream service is unavailable."),
            _ => (HttpStatusCode.InternalServerError, "An internal error occurred.")
        };

        context.Response.StatusCode = (int)statusCode;

        var payload = new
        {
            error = message,
            traceId = context.TraceIdentifier,
            // Only expose details in Development
            detail = _env.IsDevelopment() ? ex.Message : null,
            timestamp = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}