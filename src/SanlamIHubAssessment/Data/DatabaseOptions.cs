namespace SanlamIHubAssessment.Data;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Applies pending EF Core migrations on startup. Enabled in appsettings.Development.json only.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }
}
