using Microsoft.AspNetCore.Mvc;
using SanlamIHubAssessment.Data;
using SanlamIHubAssessment.Models;
using SanlamIHubAssessment.Observability;

namespace SanlamIHubAssessment.Controllers;

[ApiController]
[Route("activities")]
public class ActivitiesController(
    ActivityRepository repository,
    IngestMetrics metrics,
    TimeProvider clock,
    ILogger<ActivitiesController> logger) : ControllerBase
{
    /// <summary>
    /// Ingestion is synchronous, so the activity is already in the statement when this returns.
    /// Sending the same activity again returns 200 with "ignored". Upstream systems retry, and a retry isn't an error.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ActivityResponse>> Post(ActivityRequest request, CancellationToken ct)
    {
        var activity = new Activity
        {
            Source = request.Source!,
            ExternalId = request.ExternalId!,
            Version = request.Version!.Value,
            IntermediaryId = request.IntermediaryId!,
            ActivityType = request.ActivityType!,
            OccurredAt = request.OccurredAt!.Value,
            Amount = request.Amount!.Value,
            Currency = request.Currency!
        };

        UpsertOutcome outcome;
        try
        {
            outcome = await repository.UpsertAsync(activity, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) // a caller hanging up isn't our failure
        {
            metrics.Failed();
            throw;
        }

        // Processing delay: how long after the activity happened it reached the statement.
        var delay = clock.GetUtcNow() - activity.OccurredAt;
        metrics.Saved(outcome, delay);

        logger.LogInformation("Activity {Source}/{ExternalId} v{Version} for {IntermediaryId}: {Outcome} ({DelaySeconds:F0}s after it occurred)",
            activity.Source, activity.ExternalId, activity.Version, activity.IntermediaryId, outcome, delay.TotalSeconds);

        return Ok(new ActivityResponse(
            outcome.ToString().ToLowerInvariant(), activity.Source, activity.ExternalId, activity.Version));
    }
}
