using System.ComponentModel.DataAnnotations;

namespace SanlamIHubAssessment.Models;

/// <summary>
/// An activity as sent by an upstream system. Value types are nullable so that a missing field fails validation
/// instead of quietly becoming 0. [ApiController] turns validation failures into a 400 response.
/// </summary>
public class ActivityRequest : IValidatableObject
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    // Largest value numeric(18,2) can hold. Checked in Validate() instead of with [Range], because [Range]
    // parses its limits using the server's culture and throws where the decimal separator is a comma.
    private const decimal MaxAbsAmount = 9_999_999_999_999_999.99m;

    [Required, StringLength(100)]
    public string? Source { get; init; }

    [Required, StringLength(100)]
    public string? ExternalId { get; init; }

    [Required, Range(1, long.MaxValue)]
    public long? Version { get; init; }

    [Required, StringLength(100)]
    public string? IntermediaryId { get; init; }

    [Required, StringLength(100)]
    public string? ActivityType { get; init; }

    [Required]
    public DateTimeOffset? OccurredAt { get; init; }

    [Required]
    public decimal? Amount { get; init; }

    // Only ZAR is supported for now. Add codes here as IHub takes on other currencies.
    [Required, AllowedValues("ZAR", ErrorMessage = "Currency must be ZAR.")]
    public string? Currency { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var now = (context.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System).GetUtcNow();

        if (OccurredAt > now + AllowedClockSkew)
            yield return new ValidationResult("OccurredAt cannot be in the future.", [nameof(OccurredAt)]);

        if (Amount is { } amount && decimal.Round(amount, 2) != amount)
            yield return new ValidationResult("Amount cannot have more than 2 decimal places.", [nameof(Amount)]);
        else if (Amount is { } value && Math.Abs(value) > MaxAbsAmount)
            yield return new ValidationResult("Amount is out of range.", [nameof(Amount)]);
    }
}
