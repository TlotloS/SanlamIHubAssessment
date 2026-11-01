using Microsoft.Extensions.Diagnostics.HealthChecks;
using SanlamIHubAssessment.Data;

namespace SanlamIHubAssessment.Observability;

/// <summary>
/// Readiness check. The service can't ingest or return statements without the database, so it reports
/// unhealthy when the database can't be reached.
/// </summary>
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    // Load balancers poll often, so give up quickly instead of leaving the probe hanging on a dead database.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database check timed out.");
        }
    }
}
