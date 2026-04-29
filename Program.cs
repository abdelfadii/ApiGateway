using SirmarocGateway.Configuration;
using SirmarocGateway.Extensions;
using SirmarocGateway.Middleware;
using SirmarocGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ─── Services ────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// YARP Reverse Proxy (core of the API Gateway)
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// JWT Authentication (delegates to Auth microservice)
builder.Services.AddGatewayAuthentication(builder.Configuration);

// Authorization policies
builder.Services.AddGatewayAuthorization();

// Rate limiting
builder.Services.AddGatewayRateLimiting(builder.Configuration);

// HTTP client for token introspection with the Auth service
builder.Services.AddHttpClient<ITokenValidationService, TokenValidationService>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:Auth:BaseUrl"]
            ?? throw new InvalidOperationException("Auth service base URL is not configured.")
    );
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Distributed cache (Redis in prod, in-memory for local dev)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
else
    builder.Services.AddDistributedMemoryCache();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<GatewayHealthCheck>("gateway");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("SirmarocCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? ["http://localhost:3000"];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// ─── Middleware Pipeline ──────────────────────────────────────────────────────

// 1. Global error handling — always first
app.UseMiddleware<GlobalExceptionMiddleware>();

// 2. Request/Response logging
app.UseMiddleware<RequestLoggingMiddleware>();

// 3. Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// 4. CORS
app.UseCors("SirmarocCors");

// 5. Rate limiting
app.UseRateLimiter();

// 6. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 7. Health endpoint (bypasses auth)
app.MapHealthChecks("/health");

// 8. Local gateway controllers (e.g., flights proxy)
app.MapControllers();

// 9. YARP reverse proxy — handles all routing to microservices
app.MapReverseProxy();

app.Run();
