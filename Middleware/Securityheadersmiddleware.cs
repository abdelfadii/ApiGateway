namespace SirmarocGateway.Middleware;

/// <summary>
/// Adds security-related HTTP response headers to every outbound response.
/// These headers are not added by downstream microservices — the Gateway
/// is the right place to enforce them uniformly.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent browsers from sniffing MIME types
        headers["X-Content-Type-Options"] = "nosniff";

        // Basic XSS protection for older browsers
        headers["X-XSS-Protection"] = "1; mode=block";

        // Prevent clickjacking
        headers["X-Frame-Options"] = "DENY";

        // Referrer policy
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Permissions policy (disable browser features not needed)
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        // Remove server disclosure header
        headers.Remove("Server");

        // Identify the gateway in responses (useful for debugging)
        headers["X-Gateway"] = "SirmarocGateway/1.0";

        await _next(context);
    }
}