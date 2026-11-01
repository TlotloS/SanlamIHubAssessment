using Microsoft.EntityFrameworkCore;

namespace SanlamIHubAssessment.Data;

public enum UpsertOutcome
{
    Created,
    Updated,
    Ignored
}

public record CurrencyTotalRow(string Currency, decimal GrossAmount, long ActivityCount, DateTimeOffset LastUpdated);

public class ActivityRepository(AppDbContext db)
{
    /// <summary>
    /// Saves the activity, or replaces the stored one if this version is newer, in a single database command.
    /// Doing the check and the save together means duplicates, late arrivals and simultaneous requests
    /// can't cause double counting.
    ///
    /// (EF Core can't do this in one step, so this uses SQL directly.)
    /// </summary>
    public async Task<UpsertOutcome> UpsertAsync(Activity activity, CancellationToken ct)
    {
        var occurredAt = activity.OccurredAt.ToUniversalTime();

        // Returns [true] if inserted, [false] if updated, or an empty list if nothing changed.
        // The column must be named "Value" because EF Core's SqlQuery<bool> reads that name.
        var savedRows = await db.Database.SqlQuery<bool>($"""
            -- 1. Try to insert the activity as new.
            INSERT INTO activity (source, external_id, version, intermediary_id, activity_type, occurred_at, amount, currency)
            VALUES ({activity.Source}, {activity.ExternalId}, {activity.Version}, {activity.IntermediaryId},
                    {activity.ActivityType}, {occurredAt}, {activity.Amount}, {activity.Currency})

            -- 2. If it already exists, overwrite it with the new values...
            ON CONFLICT (source, external_id) DO UPDATE SET
                version         = EXCLUDED.version,
                intermediary_id = EXCLUDED.intermediary_id,
                activity_type   = EXCLUDED.activity_type,
                occurred_at     = EXCLUDED.occurred_at,
                amount          = EXCLUDED.amount,
                currency        = EXCLUDED.currency,
                updated_at      = now()

            -- 3. ...but only when the incoming version is newer. Duplicates and older versions change nothing.
            WHERE activity.version < EXCLUDED.version

            -- 4. Report whether the row was inserted (true) or updated (false). xmax is a Postgres system
            --    column that is 0 on a freshly inserted row.
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        if (savedRows.Count == 0)
            return UpsertOutcome.Ignored;

        var wasInserted = savedRows[0];
        return wasInserted ? UpsertOutcome.Created : UpsertOutcome.Updated;
    }

    /// <summary>Totals per currency for [from, to), using the latest version of each activity.</summary>
    public async Task<List<CurrencyTotalRow>> GetTotalsAsync(
        string intermediaryId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        return await db.Activities
            .Where(a => a.IntermediaryId == intermediaryId && a.OccurredAt >= fromUtc && a.OccurredAt < toUtc)
            .GroupBy(a => a.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencyTotalRow(g.Key, g.Sum(a => a.Amount), g.LongCount(), g.Max(a => a.UpdatedAt)))
            .ToListAsync(ct);
    }
}
