namespace SanlamIHubAssessment.Models;

/// <summary>Outcome is "created", "updated", or "ignored" when the submission was a duplicate or an older version.</summary>
public record ActivityResponse(string Outcome, string Source, string ExternalId, long Version);
