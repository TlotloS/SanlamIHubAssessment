using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SanlamIHubAssessment.Data;
using SanlamIHubAssessment.Tests.Infrastructure;

namespace SanlamIHubAssessment.Tests;

// Proves the test plumbing works before any business tests exist.
[Collection(ApiCollection.Name)]
public sealed class SmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Api_starts_and_serves_requests()
    {
        var response = await fixture.Client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_uses_the_test_container_database()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Guards against silently testing against a local/compose Postgres instead of the container.
        Assert.Equal(fixture.ConnectionString, db.Database.GetConnectionString());
        Assert.True(await db.Database.CanConnectAsync());
    }
}
