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

    /// <summary>The upstream system that sent the activity.</summary>
    /// <example>life-admin</example>
    [Required, StringLength(100)]
    public string? Source { get; init; }

    /// <summary>The upstream system's ID for the activity. Together with Source it identifies the activity.</summary>
    /// <example>C-1001</example>
    [Required, StringLength(100)]
    public string? ExternalId { get; init; }

    /// <summary>Goes up each time the upstream system revises the activity. The highest version wins.</summary>
    /// <example>1</example>
    [Required, Range(1, long.MaxValue)]
    public long? Version { get; init; }

    /// <summary>The intermediary (broker) the activity belongs to.</summary>
    /// <example>BRK-001</example>
    [Required, StringLength(100)]
    public string? IntermediaryId { get; init; }

    /// <summary>What kind of activity it is, for example commission or fee.</summary>
    /// <example>commission</example>
    [Required, StringLength(100)]
    public string? ActivityType { get; init; }

    /// <summary>When the activity happened (not when it was sent). Must include a UTC offset.</summary>
    /// <example>2026-11-01T09:30:00+02:00</example>
    [Required]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>Up to 2 decimal places. Negative values are allowed for reversals.</summary>
    /// <example>1250.00</example>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>Only ZAR is supported for now.</summary>
    /// <example>ZAR</example>
    // Add codes here as IHub takes on other currencies.
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
