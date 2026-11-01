using Microsoft.AspNetCore.Mvc;
using SanlamIHubAssessment.Data;
using SanlamIHubAssessment.Models;

namespace SanlamIHubAssessment.Controllers;

[ApiController]
[Route("activities")]
public class ActivitiesController(ActivityRepository repository, ILogger<ActivitiesController> logger) : ControllerBase
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

        var outcome = await repository.UpsertAsync(activity, ct);

        logger.LogInformation("Activity {Source}/{ExternalId} v{Version} for {IntermediaryId}: {Outcome}",
            activity.Source, activity.ExternalId, activity.Version, activity.IntermediaryId, outcome);

        return Ok(new ActivityResponse(
            outcome.ToString().ToLowerInvariant(), activity.Source, activity.ExternalId, activity.Version));
    }
}
