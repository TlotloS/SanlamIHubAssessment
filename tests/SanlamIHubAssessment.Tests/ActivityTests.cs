using System.Net;
using System.Net.Http.Json;
using SanlamIHubAssessment.Tests.Infrastructure;

namespace SanlamIHubAssessment.Tests;

// Tests the Task 1 API described in SOLUTION.md.
[Collection(ApiCollection.Name)]
public sealed class ActivityTests(ApiFixture fixture)
{
    // A fixed date in the past, so results don't depend on today's date and never hit the future-date check.
    private static readonly DateTimeOffset WindowStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = WindowStart.AddDays(1);
    private static readonly DateTimeOffset InWindow = WindowStart.AddHours(9);

    [Fact]
    public async Task PostActivity_NewActivity_AppearsInStatement()
    {
        var intermediaryId = NewIntermediaryId();

        var response = await PostActivity(NewActivity(intermediaryId, "A-1", version: 1, amount: 100m));

        Assert.Equal("created", response.Outcome);
        var statement = await GetStatement(intermediaryId);
        AssertZarTotal(statement, expectedAmount: 100m, expectedCount: 1);
    }

    [Fact]
    public async Task PostActivity_SameActivitySubmittedTwice_CountedOnce()
    {
        var intermediaryId = NewIntermediaryId();
        var activity = NewActivity(intermediaryId, "A-1", version: 1, amount: 100m);

        await PostActivity(activity);
        var second = await PostActivity(activity);

        Assert.Equal("ignored", second.Outcome);
        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 100m, expectedCount: 1);
    }

    [Fact]
    public async Task PostActivity_OlderVersionArrivesLate_DoesNotOverwriteNewer()
    {
        var intermediaryId = NewIntermediaryId();

        await PostActivity(NewActivity(intermediaryId, "B-1", version: 3, amount: 250m));
        var v1 = await PostActivity(NewActivity(intermediaryId, "B-1", version: 1, amount: 999m));
        var v2 = await PostActivity(NewActivity(intermediaryId, "B-1", version: 2, amount: 500m));

        Assert.Equal("ignored", v1.Outcome);
        Assert.Equal("ignored", v2.Outcome);
        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 250m, expectedCount: 1);
    }

    [Fact]
    public async Task PostActivity_NewerVersion_ReplacesStoredAmount()
    {
        var intermediaryId = NewIntermediaryId();

        await PostActivity(NewActivity(intermediaryId, "B-1", version: 1, amount: 100m));
        var v2 = await PostActivity(NewActivity(intermediaryId, "B-1", version: 2, amount: 80m));

        Assert.Equal("updated", v2.Outcome);
        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 80m, expectedCount: 1);
    }

    [Fact]
    public async Task PostActivity_ConcurrentSubmissionsForSameIntermediary_TotalIsCorrect()
    {
        var intermediaryId = NewIntermediaryId();

        await Task.WhenAll(Enumerable.Range(1, 50)
            .Select(i => PostActivity(NewActivity(intermediaryId, $"D-{i}", version: 1, amount: 10m))));

        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 500m, expectedCount: 50);
    }

    [Fact]
    public async Task PostActivity_ConcurrentVersionsOfSameActivity_HighestVersionWins()
    {
        var intermediaryId = NewIntermediaryId();

        // Sends versions 1 to 20 at once, in random order. Amount is version x 5, so version 20 should win with 100.
        await Task.WhenAll(Enumerable.Range(1, 20)
            .OrderBy(_ => Random.Shared.Next())
            .Select(v => PostActivity(NewActivity(intermediaryId, "C-1", version: v, amount: v * 5m))));

        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 100m, expectedCount: 1);
    }

    [Fact]
    public async Task GetStatement_ActivitiesOnWindowBoundaries_IncludesStartExcludesEnd()
    {
        var intermediaryId = NewIntermediaryId();

        await PostActivity(NewActivity(intermediaryId, "W-start", 1, 100m) with { OccurredAt = WindowStart });
        await PostActivity(NewActivity(intermediaryId, "W-end", 1, 999m) with { OccurredAt = WindowEnd });

        AssertZarTotal(await GetStatement(intermediaryId), expectedAmount: 100m, expectedCount: 1);
    }

    [Fact]
    public async Task PostActivity_InvalidCurrency_ReturnsBadRequest()
    {
        var activity = NewActivity(NewIntermediaryId(), "X-1", version: 1, amount: 1m) with { Currency = "rand" };

        var response = await fixture.Client.PostAsJsonAsync("/activities", activity);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Tests share one database that is never cleared, so each test uses its own intermediary.
    private static string NewIntermediaryId() => $"BRK-{Guid.NewGuid():N}";

    // (source, externalId) is unique across all intermediaries, so external IDs get the test's intermediary as a
    // prefix. Without it, "A-1" in one test would count as a resubmission of "A-1" from another test.
    private static ActivityRequest NewActivity(string intermediaryId, string externalId, long version, decimal amount) =>
        new("test-source", $"{intermediaryId}/{externalId}", version, intermediaryId, "commission", InWindow, amount, "ZAR");

    private async Task<ActivityResponse> PostActivity(ActivityRequest activity)
    {
        var response = await fixture.Client.PostAsJsonAsync("/activities", activity);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActivityResponse>())!;
    }

    private async Task<StatementResponse> GetStatement(string intermediaryId)
    {
        var url = $"/intermediaries/{intermediaryId}/statement?from={WindowStart:yyyy-MM-ddTHH:mm:ssZ}&to={WindowEnd:yyyy-MM-ddTHH:mm:ssZ}";
        var response = await fixture.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StatementResponse>())!;
    }

    private static void AssertZarTotal(StatementResponse statement, decimal expectedAmount, long expectedCount)
    {
        var zar = Assert.Single(statement.Totals);
        Assert.Equal("ZAR", zar.Currency);
        Assert.Equal(expectedAmount, zar.GrossAmount);
        Assert.Equal(expectedCount, zar.ActivityCount);
    }

    // The tests keep their own copies of the request and response shapes, so they check the HTTP contract
    // and not the API's internal classes.
    private sealed record ActivityRequest(
        string Source, string ExternalId, long Version, string IntermediaryId,
        string ActivityType, DateTimeOffset OccurredAt, decimal Amount, string Currency);

    private sealed record ActivityResponse(string Outcome);

    private sealed record StatementResponse(
        string IntermediaryId, DateTimeOffset From, DateTimeOffset To,
        IReadOnlyList<CurrencyTotal> Totals, DateTimeOffset? LastUpdated);

    private sealed record CurrencyTotal(string Currency, decimal GrossAmount, long ActivityCount);
}
