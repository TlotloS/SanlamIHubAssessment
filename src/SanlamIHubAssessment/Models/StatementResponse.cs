namespace SanlamIHubAssessment.Models;

/// <summary>Gross totals for one intermediary from From (included) to To (excluded), one entry per currency.</summary>
public record StatementResponse(
    string IntermediaryId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<CurrencyTotal> Totals,
    DateTimeOffset? LastUpdated);

public record CurrencyTotal(string Currency, decimal GrossAmount, long ActivityCount);
