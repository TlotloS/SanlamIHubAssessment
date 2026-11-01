using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SanlamIHubAssessment.Data;
using SanlamIHubAssessment.Tests.Infrastructure;

namespace SanlamIHubAssessment.Tests;

// Checks the test setup itself: the API starts and talks to the container database.
[Collection(ApiCollection.Name)]
public sealed class SmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetWeatherForecast_ApiStarted_ReturnsOk()
    {
        var response = await fixture.Client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AppDbContext_ResolvedFromApi_UsesTestContainerDatabase()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Makes sure the tests aren't accidentally running against a local or docker-compose Postgres.
        Assert.Equal(fixture.ConnectionString, db.Database.GetConnectionString());
        Assert.True(await db.Database.CanConnectAsync());
    }
}
