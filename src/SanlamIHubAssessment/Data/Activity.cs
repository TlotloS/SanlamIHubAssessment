namespace SanlamIHubAssessment.Data;

/// <summary>
/// Latest known state of one upstream activity, identified by (Source, ExternalId).
/// A newer Version overwrites the row.
/// </summary>
public class Activity
{
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public long Version { get; set; }
    public required string IntermediaryId { get; set; }
    public required string ActivityType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
