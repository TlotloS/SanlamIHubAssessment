1. I've genereated a .NET 10 web api via rider scaffold the project, with PostgreSQL 16, EF Core + Npgsql, with automigrations by default, the auto migrations will by default only in dev , configuration in appsettings, leave the test enpoint for now, everyghin esle is purelys scalldging with basic json console logging.

2. Restrucute the project to folder to allow for better cohesion and provide room for testing project.

3. Set up integration test infrastructure in tests/SanlamIHubAssessment.Tests. No business tests yet.

Context: .NET 10 Web API in src/SanlamIHubAssessment using EF Core + Npgsql, Postgres 16.
Connection string key is ConnectionStrings:Default. Migrations apply on startup when
Database:ApplyMigrationsOnStartup is true (on in Development).

Requirements:
- Use Testcontainers.PostgreSql (postgres:16 image) and Microsoft.AspNetCore.Mvc.Testing.
- One Postgres container shared across the whole test run (xUnit collection fixture),
  not one per test, to keep the suite fast.
- A WebApplicationFactory<Program> that overrides ConnectionStrings:Default with the
  container's connection string and runs in the Development environment so migrations apply.
- Expose an HttpClient from the fixture for tests to use.
- Add `public partial class Program;` to Program.cs so the factory can reference it.
- One smoke test only: GET /weatherforecast returns 200, to prove the plumbing works.
- No mocking of the database: the correctness rules live in SQL, so tests must hit real Postgres.

Keep it minimal. Explain any non-obvious choice in a short comment.

4.
