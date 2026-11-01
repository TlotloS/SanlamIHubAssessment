using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SanlamIHubAssessment.Data;

public static class DatabaseExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Default' is not configured.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        app.Logger.LogInformation("Applying database migrations");
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied");
    }
}
