using Microsoft.Extensions.Caching.Distributed;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SirmarocGateway.Services;

// ── Contract ──────────────────────────────────────────────────────────────────

public interface ITokenValidationService
{
    /// <summary>
    /// Calls the Auth microservice to introspect a JWT.
    /// Returns null if the token is invalid or the Auth service is unavailable.
    /// </summary>
    Task<TokenIntrospectionResult?> IntrospectAsync(string token, CancellationToken ct = default);
}

// ── DTO ───────────────────────────────────────────────────────────────────────

public record TokenIntrospectionResult(
    bool Active,
    string UserId,
    string Role,
    string Email
);

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Validates tokens by calling /auth/introspect on the Auth microservice.
/// Results are cached for 30 seconds to avoid hammering the Auth service on
/// every proxied request.
/// </summary>
public sealed class TokenValidationService : ITokenValidationService
{
    private readonly HttpClient _http;
    private readonly ILogger<TokenValidationService> _logger;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache _cache;

    public TokenValidationService(
        HttpClient http,
        ILogger<TokenValidationService> logger,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache cache)
    {
        _http = http;
        _logger = logger;
        _cache = cache;
    }

    public async Task<TokenIntrospectionResult?> IntrospectAsync(
        string token,
        CancellationToken ct = default)
    {
        // Cache key is the first 16 chars of the token (never log full tokens)
        var cacheKey = $"token:{token[..Math.Min(16, token.Length)]}";

        // Check local cache first
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<TokenIntrospectionResult>(cached);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/introspect");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Auth service returned {Status} for token introspection",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<TokenIntrospectionResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result is { Active: true })
            {
                // Cache the positive result for 30 seconds
                await _cache.SetStringAsync(cacheKey, json,
                    new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                    }, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to introspect token against Auth service");
            return null;
        }
    }
}