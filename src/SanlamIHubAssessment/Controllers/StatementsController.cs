using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SanlamIHubAssessment.Data;
using SanlamIHubAssessment.Models;

namespace SanlamIHubAssessment.Controllers;

[ApiController]
[Route("intermediaries/{intermediaryId}/statement")]
public class StatementsController(ActivityRepository repository) : ControllerBase
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    /// <summary>
    /// The window includes 'from' and excludes 'to', so an activity can't appear in two back-to-back statements.
    /// Totals are calculated when requested, from the latest version of each activity.
    /// </summary>
    /// <param name="intermediaryId" example="BRK-001">The intermediary to report on.</param>
    /// <param name="from" example="2026-11-01T00:00:00Z">Start of the window (included). ISO 8601 with a timezone.</param>
    /// <param name="to" example="2026-11-02T00:00:00Z">End of the window (excluded). ISO 8601 with a timezone.</param>
    /// <param name="ct">Cancelled if the caller disconnects.</param>
    [HttpGet]
    public async Task<ActionResult<StatementResponse>> Get(
        string intermediaryId,
        [FromQuery, BindRequired] DateTimeOffset from,
        [FromQuery, BindRequired] DateTimeOffset to,
        CancellationToken ct)
    {
        if (to <= from)
            ModelState.AddModelError(nameof(to), "'to' must be after 'from'.");
        else if (to - from > MaxWindow)
            ModelState.AddModelError(nameof(to), "The window cannot be longer than 366 days.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var rows = await repository.GetTotalsAsync(intermediaryId, from, to, ct);
        var result = rows.Select(r => new CurrencyTotal(r.Currency, r.GrossAmount, r.ActivityCount)).ToList();
        DateTimeOffset? lastUpdatedDate = rows.Count == 0 ? null : rows.Max(r => r.LastUpdated);
        
        return Ok(new StatementResponse(
            intermediaryId,
            from,
            to,
            result,
            lastUpdatedDate
            ));
    }
}
