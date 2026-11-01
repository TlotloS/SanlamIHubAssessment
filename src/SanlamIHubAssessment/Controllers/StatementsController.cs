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

        return Ok(new StatementResponse(
            intermediaryId,
            from,
            to,
            rows.Select(r => new CurrencyTotal(r.Currency, r.GrossAmount, r.ActivityCount)).ToList(),
            rows.Count == 0 ? null : rows.Max(r => r.LastUpdated)));
    }
}
