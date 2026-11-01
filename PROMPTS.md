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

Prompt 7. Make the failing tests pass: Activity entity and EF configuration, the initial
   migration, POST /activities with the upsert, then the statement endpoint.)

   What the AI did: added the entity, configuration and migration, a repository with the
   upsert as one raw SQL statement (INSERT ... ON CONFLICT ... WHERE version is higher), and
   the two controllers. The tests caught three problems on the way to green:
   - [Range] on the amount parsed its limits using my machine's culture (comma decimal
     separator) and crashed every request. Moved the check into code.
   - The tests reused external IDs like "A-1" across tests, so a "new" activity in one test
     was a resubmission from another. The API was right; the tests were scoped per
     intermediary.
   - An OrderBy that EF couldn't translate to SQL. Moved it before the projection.

   Follow-ups I asked for:
   - Use Models instead of Contracts
   - Is there an easier way to define the currency than a regular expression? I want easy to
     read data, and for now I'll only use ZAR. (Now [AllowedValues("ZAR")].)
   - How can you simplify the upsert for ease of reading? (Numbered SQL comments for each step,
     and plain if statements instead of a list pattern.)

   What I checked: all tests green, and ran the concurrency tests several times to make sure
   they weren't passing by luck.

Prompt 8. Let's begin by adding the health checks.

   What the AI did: /health/live (process is up, checks nothing else) and /health/ready (can
   reach the database, with a 2-second timeout).

   What I checked: in Docker with the database up and then stopped. Ready went from 200 to 503,
   live stayed 200, so a database outage takes instances out of the load balancer without
   restarting them.

Prompt 9. Should I add swagger for ease of use? Quickly add it.
   Follow-up, after typing a date in the wrong format on the statement endpoint: I want all
   the input fields to have placeholders, e.g. { "source": "test-1", "externalId": "001",
   "version": 1, "intermediaryId": "insurance-001", "activityType": "insurance",
   "occurredAt": "2026-11-02T04:12:20.217Z", "amount": 500, "currency": "ZAR" }

   What the AI did: added Swagger UI (development only) on top of the OpenAPI document the
   project already generated, then example values and a short description for every input
   using XML doc comments. The example statement window matches the example activity, so
   a reviewer can run the POST and then the GET and see a real total straight away.

Prompt 10. Add minimal ingestion metrics for Task 2, so an operator can answer "Is it working?
   How fast? What broke?". Keep it as simple as possible.
   - An IngestMetrics singleton counting created, updated, ignored and failed, plus the
     last processing delay in seconds (save time minus occurredAt).
   - Use Interlocked for the counters, since requests run at the same time.
   - Show it as plain JSON at GET /metrics.
   - In ActivitiesController, call metrics.Saved(outcome, delay) after the upsert. If the
     upsert throws, call metrics.Failed() and rethrow, but don't count a cancelled request.
   - Add the delay to the existing activity log line. Keep it out of the API response.
   - No tests: I'll verify by hand (new, duplicate, newer version, database stopped).

   Decisions I made before this prompt: no custom correlation ID middleware, because the
   built-in W3C TraceId is already on every log line; no tests for observability, because
   the automated tests focus on the correctness rules; and a single "last delay" number
   rather than percentiles, to keep it simple.

   What I checked: new, duplicate, newer version, then stopped the database and posted again.
   /metrics showed created 1, ignored 1, updated 1, failed 1.

   Follow-up: Simplify the IngestMetrics for readability

Prompt 11. Reword this for me? (written in Azure terms:)

   > Despite not being to familiar with AWS, I'm familiar with azure. My first inkling would be
   > to align the infra with the current hosting strategy at hand that the team / org already use.
   > 1) I would host it on AKS as this would allow for scalability as the platform grows under
   >    load, spinning up multiple instances so load is distributed across pods, taking advantage
   >    of the load balancer and readiness checks, along with high availability.
   > 2) From an activity ingestion perspective, I'd use RabbitMQ / Service Bus with MassTransit,
   >    which creates a queue as opposed to HTTP, so activities are kept when the system is down
   >    and consumption resumes once it's back up.
   > 3) I would host the database on a managed instance.
   > 4) Rollout: run the DB migration before deploying the service, automated in the CD pipeline.
   > A single Docker image deployed in dev, tested and promoted across all environments to
   > production. Automated smoke tests, CI build and integration tests. Alerts when health checks
   > fail. Secrets in a key vault. Autoscaling.
   > All of these using the equivalent infrastructure for AWS.

   Follow-ups: Do you agree with all this? (AI challenged EKS as heavy for one small service,
   flagged checking MassTransit's licence, and noted that a queue makes ingestion asynchronous.)
   What is ECS? Does it allow for HA, e.g. multiple pods? Okay perfect, then update it.
   Should I not mention how I would manage statements and metrics? Maybe generate a proper
   diagram as well.

   What I decided: ECS on Fargate as the default, EKS if the organisation already runs
   Kubernetes. A neutral SQS consumer (AWS SDK or MassTransit, licence permitting) that calls
   the same upsert.

   What the AI did: reworded my plan, added an Azure to AWS mapping table, capacity and failure
   mode notes, and drew docs/architecture.svg.

Prompt 12. Complete the remaining SOLUTION.md sections and create the README.

   What the AI did: wrote "how I guarantee correct totals" (with a table mapping each rule to
   its test), growth to more activity types and sources, the Task 2 observability section
   (signals, two alerts with thresholds and operator actions), and the trade-offs. Fixed small
   errors (typo, ZAR only, a traceId claim that was only true for validation errors). Created
   README.md with run instructions, URLs, curl examples and things to try.

   What I checked: ran every README step against Docker and the tests.
