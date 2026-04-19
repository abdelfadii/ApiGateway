using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SirmarocGateway.Configuration;

/// <summary>
/// Basic liveness health check for the Gateway itself.
/// YARP's built-in active health checks probe each downstream cluster separately.
/// </summary>
public sealed class GatewayHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // The gateway is healthy as long as this code is reachable
        return Task.FromResult(HealthCheckResult.Healthy("Sirmmaroc API Gateway is running."));
    }
}