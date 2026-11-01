using SanlamIHubAssessment.Data;

namespace SanlamIHubAssessment.Observability;

/// <summary>What GET /metrics returns.</summary>
public record MetricsSnapshot(long Created, long Updated, long Ignored, long Failed, double LastDelaySeconds);

/// <summary>
/// Counts what happens to incoming activities, for GET /metrics. It's a singleton, so all requests share one set
/// of counts. The counts live in memory and reset on restart. In production they'd be sent to CloudWatch.
/// </summary>
public class IngestMetrics
{
    // Requests run at the same time, so the counters are only changed with Interlocked, which never loses a count.
    private long _created;
    private long _updated;
    private long _ignored;
    private long _failed;

    // How long after it happened the most recent new or updated activity was saved.
    private double _lastDelaySeconds;

    public void Saved(UpsertOutcome outcome, TimeSpan delay)
    {
        if (outcome == UpsertOutcome.Ignored)
        {
            // A duplicate or older version: nothing new arrived, so there's no delay to record.
            Interlocked.Increment(ref _ignored);
            return;
        }

        if (outcome == UpsertOutcome.Created)
            Interlocked.Increment(ref _created);
        else
            Interlocked.Increment(ref _updated);

        _lastDelaySeconds = Math.Max(0, Math.Round(delay.TotalSeconds));
    }

    public void Failed() => Interlocked.Increment(ref _failed);

    public MetricsSnapshot Snapshot() => new(
        Created: Interlocked.Read(ref _created),
        Updated: Interlocked.Read(ref _updated),
        Ignored: Interlocked.Read(ref _ignored),
        Failed: Interlocked.Read(ref _failed),
        LastDelaySeconds: _lastDelaySeconds);
}
