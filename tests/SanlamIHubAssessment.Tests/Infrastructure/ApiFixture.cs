using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace SanlamIHubAssessment.Tests.Infrastructure;

/// <summary>
/// Starts one Postgres container and one in-memory API host, shared by all tests.
/// It's a real database rather than a mock or the EF in-memory provider, because the rules being
/// tested (the primary key, ON CONFLICT and row locking) only exist in Postgres.
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
            // Development applies migrations on startup, so the container gets the current schema.
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

// Tests share the database and it is never cleared, so each test has to use its own IDs
// and can't assume the table is empty.
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
