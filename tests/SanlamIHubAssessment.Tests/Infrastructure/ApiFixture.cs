using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace SanlamIHubAssessment.Tests.Infrastructure;

/// <summary>
/// Starts one real Postgres container and one in-memory API host for the whole test run.
/// Real Postgres (not a mock or the EF in-memory provider) because the correctness rules
/// live in SQL: the primary key, ON CONFLICT and row locking.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Development applies migrations on startup, so the schema matches the current model.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        });
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

// The database is shared and never reset between tests, so each test must use its own
// identifiers rather than assume an empty table.
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
