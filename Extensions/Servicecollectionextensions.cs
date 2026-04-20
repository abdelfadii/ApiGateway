using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace SirmarocGateway.Extensions;

public static class ServiceCollectionExtensions
{
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
            options.RequireHttpsMetadata = false;
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

            // Forward the token to downstream microservices via headers
            options.Events = new JwtBearerEvents
{
                OnTokenValidated = context =>
                {
                    var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;

                    if (!string.IsNullOrWhiteSpace(userId))
                        context.HttpContext.Request.Headers["X-User-Id"] = userId;

                    if (!string.IsNullOrWhiteSpace(role))
                        context.HttpContext.Request.Headers["X-User-Role"] = role;

                    return Task.CompletedTask;
                }
            };

        });

        return services;
    }

    public static IServiceCollection AddGatewayAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AAA", policy => policy.RequireAssertion(_ => true));

            options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

            options.AddPolicy("admin", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole("Admin"));

            options.AddPolicy("provider", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole("Provider", "Admin"));


            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

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
            options.AddSlidingWindowLimiter("global", limiterOptions =>
            {
                limiterOptions.PermitLimit = permitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
                limiterOptions.SegmentsPerWindow = 6;
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = queueLimit;
            });

            options.AddFixedWindowLimiter("auth", limiterOptions =>
            {
                limiterOptions.PermitLimit = 10;
                limiterOptions.Window = TimeSpan.FromSeconds(60);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }
}