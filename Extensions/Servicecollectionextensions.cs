using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace SirmarocGateway.Extensions;

public static class ServiceCollectionExtensions
{
    // ── JWT Authentication ────────────────────────────────────────────────────
    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var secretKey = jwtSection["SecretKey"]
            ?? throw new InvalidOperationException("JWT SecretKey is not configured.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false; // set true in production behind TLS termination
            options.SaveToken = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidAudience = jwtSection["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            // Forward user claims to downstream microservices
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;

                    if (userId is not null)
                        context.HttpContext.Request.Headers["X-User-Id"] = userId;

                    if (role is not null)
                        context.HttpContext.Request.Headers["X-User-Role"] = role;

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }

    // ── Authorization Policies ────────────────────────────────────────────────
    public static IServiceCollection AddGatewayAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Public routes — no auth required
            options.AddPolicy("AAA", policy => policy.RequireAssertion(_ => true));

            // Any authenticated user
            options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

            // Admin only
            options.AddPolicy("admin", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole("Admin"));

            // Provider (fournisseur) access
            options.AddPolicy("provider", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("role", "provider", "admin"));

            // Fallback: deny everything unless explicitly allowed
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rl = configuration.GetSection("RateLimiting");
        int permitLimit = rl.GetValue<int>("PermitLimit", 100);
        int windowSeconds = rl.GetValue<int>("WindowSeconds", 60);
        int queueLimit = rl.GetValue<int>("QueueLimit", 10);

        services.AddRateLimiter(options =>
        {
            // Default sliding-window policy applied to every request
            options.AddSlidingWindowLimiter("global", limiterOptions =>
            {
                limiterOptions.PermitLimit = permitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
                limiterOptions.SegmentsPerWindow = 6;
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = queueLimit;
            });

            // Tighter policy for the auth endpoints (anti-brute-force)
            options.AddFixedWindowLimiter("auth", limiterOptions =>
            {
                limiterOptions.PermitLimit = 10;
                limiterOptions.Window = TimeSpan.FromSeconds(60);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<RateLimiterOptions>>();

                logger.LogWarning(
                    "Rate limit exceeded for IP {Ip} on {Path}",
                    context.HttpContext.Connection.RemoteIpAddress,
                    context.HttpContext.Request.Path);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsync(
                    """{"error":"Too many requests. Please slow down and try again later."}""",
                    cancellationToken);
            };
        });

        return services;
    }
}
