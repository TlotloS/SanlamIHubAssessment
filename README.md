# IHub intermediary activity statement

A small .NET 10 service that ingests activities from upstream systems and returns a statement of an
intermediary's gross total over a time window. It handles duplicate, out-of-order and concurrent
submissions without double counting.

- Design, trade-offs, observability and the AWS plan: [SOLUTION.md](SOLUTION.md)
- How I used AI: [PROMPTS.md](PROMPTS.md)

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/), running
- [.NET 10 SDK](https://dotnet.microsoft.com/download), only needed to run the tests

## Run it

From the repository root:

```bash
docker compose up --build
```

This starts PostgreSQL 16 and the API. The database schema is created automatically on startup.
When the logs show `Application started`, the API is available at `http://localhost:8080`.

| What | URL |
|---|---|
| Swagger UI (try the API in the browser) | http://localhost:8080/swagger |
| Readiness check | http://localhost:8080/health/ready |
| Liveness check | http://localhost:8080/health/live |
| Metrics | http://localhost:8080/metrics |

To stop it, press `Ctrl+C`, or run `docker compose down`. Add `-v` to also delete the database.

## Try it

The quickest way is Swagger: every field is prefilled with an example. Execute `POST /activities`,
then `GET /intermediaries/{intermediaryId}/statement`, and the statement shows the activity's amount.

With curl:

```bash
curl -X POST http://localhost:8080/activities \
  -H "Content-Type: application/json" \
  -d '{"source":"life-admin","externalId":"C-1001","version":1,"intermediaryId":"BRK-001","activityType":"commission","occurredAt":"2026-11-01T09:30:00+02:00","amount":1250.00,"currency":"ZAR"}'
```

```bash
curl "http://localhost:8080/intermediaries/BRK-001/statement?from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z"
```

Things to try:

- Send the same activity again: the outcome is `ignored` and the total doesn't change.
- Send it with `"version": 2` and a different amount: the outcome is `updated` and the total changes.
- Then send `"version": 1` again: it's `ignored`, because an older version never overwrites a newer one.
- Send `"currency": "USD"`: a `400` explains that only ZAR is accepted.
- Run `docker compose stop postgres` and post again: the request fails, `failed` goes up on
  `/metrics`, and `/health/ready` reports `Unhealthy`. Run `docker compose start postgres` to recover.

## Run the tests

Docker Desktop must be running: the integration tests start their own PostgreSQL container, so they
don't touch the database used by `docker compose`.

```bash
dotnet test
```

## Project layout

```
src/SanlamIHubAssessment/          the API
  Controllers/                     POST /activities, GET .../statement
  Data/                            EF Core model, migrations, and the upsert
  Models/                          request and response shapes
  Observability/                   health check and metrics
tests/SanlamIHubAssessment.Tests/  integration tests against a real PostgreSQL container
docs/architecture.svg              AWS architecture diagram
```
