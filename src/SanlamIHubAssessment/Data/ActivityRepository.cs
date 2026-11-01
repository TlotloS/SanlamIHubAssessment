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
    /// Inserts the activity, or overwrites the stored row when this version is newer, in a single SQL statement.
    /// A duplicate or older version fails the WHERE clause and changes nothing. Postgres makes concurrent writes
    /// to the same key wait on the primary key, so the app doesn't need its own locking.
    /// This is the only raw SQL in the project. EF Core has no conditional upsert, and reading first and then
    /// calling SaveChanges would race under concurrent requests.
    /// RETURNING produces no row when the update is skipped, and xmax = 0 means the row was just inserted.
    /// </summary>
    public async Task<UpsertOutcome> UpsertAsync(Activity activity, CancellationToken ct)
    {
        var occurredAt = activity.OccurredAt.ToUniversalTime();

        var inserted = await db.Database.SqlQuery<bool>($"""
            INSERT INTO activity (source, external_id, version, intermediary_id, activity_type, occurred_at, amount, currency)
            VALUES ({activity.Source}, {activity.ExternalId}, {activity.Version}, {activity.IntermediaryId},
                    {activity.ActivityType}, {occurredAt}, {activity.Amount}, {activity.Currency})
            ON CONFLICT (source, external_id) DO UPDATE SET
                version         = EXCLUDED.version,
                intermediary_id = EXCLUDED.intermediary_id,
                activity_type   = EXCLUDED.activity_type,
                occurred_at     = EXCLUDED.occurred_at,
                amount          = EXCLUDED.amount,
                currency        = EXCLUDED.currency,
                updated_at      = now()
            WHERE activity.version < EXCLUDED.version
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        return inserted switch
        {
            [] => UpsertOutcome.Ignored,
            [true] => UpsertOutcome.Created,
            _ => UpsertOutcome.Updated
        };
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
