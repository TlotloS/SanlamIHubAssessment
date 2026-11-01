Prompt 1. I've genereated a .NET 10 web api via rider scaffold the project, with PostgreSQL 16, EF Core + Npgsql, with automigrations by default, the auto migrations will by default only in dev , configuration in appsettings, leave the test enpoint for now, everyghin esle is purelys scalldging with basic json console logging.

Prompt 2. Restrucute the project to folder to allow for better cohesion and provide room for testing project.

Prompt 3. Set up integration test infrastructure in tests/SanlamIHubAssessment.Tests. No business tests yet.

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

Prompt 4. I'm building a service that ingests activities (commissions, fees, adjustments) from
upstream systems and produces a statement per intermediary over a period. Poke holes
in my approach, no code.

Input is messy: duplicates from retries, corrections arriving before originals,
concurrent writes across instances, negative amounts, multiple currencies.

My approach (.NET 10, Postgres 16, EF Core):
- (source, externalId) is the primary key, one row per activity
- An upsert that only overwrites when the incoming version is higher
- No history, no locks, no queue
- POST returns 200 with created/updated/ignored
- Statement by occurredAt, window [from, to), totals per currency

Tell me:
1. Any order of requests that breaks the total? If not, why it holds.
2. Weakest assumptions and how I'd notice them failing.
3. What I haven't decided yet.
4. Edge cases to test.

Prompt 5:
We've finalised the approach. Write it up as Task 1 in SOLUTION.md, short and in
first person. No code.

Design:
- (source, externalId) is the primary key, one row per activity. Upstream must send a
  stable ID and an increasing version.
- An upsert that only overwrites when the incoming version is higher. Postgres row
  locking makes this safe for duplicates, out-of-order versions and concurrent writes
  across instances, with no app locks or queue.
- Same version with a different payload: first one wins. Flagging it is a next step.
- No history. Newer versions overwrite. A version table can be added later without
  changing the API.
- POST /activities returns 200 with created, updated or ignored, and 400 for invalid
  input. Retries are safe.
- Synchronous: once POST returns, it's in the statement.
- Money is numeric(18,2) and can be negative. Times are stored in UTC. occurredAt can
  be up to 5 minutes in the future.
- A newer version can change occurredAt or intermediaryId, and the activity moves with
  it.
- GET /intermediaries/{id}/statement?from=&to= uses occurredAt, window [from, to).
  Totals per currency, no conversion. lastUpdated is null for an empty window.
- Index on (intermediary_id, occurred_at).

Sections:
- the problem
- API
- data model
- assumptions
- how I guarantee correct totals
- trade-offs

Prompt 6. Write failing integration tests for the Task 1 rules in SOLUTION.md, before any of the
   API code exists. Creat tests for the following scenarios:
   - an activity posted must show up in the statement
   - the same activity sent twice is only counted once
   - an older version that arrives late doesn't overwrite the newer one
   - 50 activities sent at the same time still add up to the right total
   - the time window includes the start time and excludes the end time
   - an invalid currency gets rejected
   All of them fail for now with "not found", because the endpoints don't exist yet.

   What the AI added: two tests I hadn't listed. One checks that a newer version replaces
   the stored amount. The other sends 20 versions of the same activity at once, in random
   order, and checks the highest version wins. I kept both because they cover cases I
   would have missed.

   What I changed: renamed every test to my usual MethodName_Scenario_ExpectedResult format.

   What I checked: I worked out each expected total by hand. For example, versions 3, 1, 2
   arriving in that order should leave 250, because version 3 is the newest.
