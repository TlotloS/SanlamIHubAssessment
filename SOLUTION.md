# IHub intermediary activity statement

## The problem

IHub turns activities from product systems (commissions, fees, adjustments) into a statement of what
each intermediary earned over a period. Summing amounts is easy.

The tricky part is the input: activities can arrive more than once, corrections can arrive before the original, and submissions for the same
intermediary can land at the same time during peaks.

All three cases reduce to one requirement: each upstream activity is stored once, and only its latest
version counts. I enforce that in the database rather than in application code, so it holds regardless
of how many API instances run or what order requests arrive in, without locks or a queue.

---

## Task 1: Ingestion and statement

### API

One ingestion endpoint and one retrieval endpoint, both HTTP.

#### `POST /activities`
```json
{
  "source": "life-admin",
  "externalId": "C-1001",
  "version": 1,
  "intermediaryId": "BRK-001",
  "activityType": "commission",
  "occurredAt": "2026-11-01T09:30:00+02:00",
  "amount": 1250.00,
  "currency": "ZAR"
}
```

- `source` + `externalId` identify the activity uniquely.
- `version` increases when upstream revises the activity; I use it to decide which submission wins.
- `occurredAt` is when the activity happened, not when it arrived, and must include a UTC offset.
- `amount` allows 2 decimal places and can be negative (clawbacks, reversals). `currency` is an ISO 4217 code; only `ZAR` is accepted for now.

I return **200 OK** with an `outcome` of `created`, `updated` (newer version applied) or `ignored`
(duplicate or older version), and **400** with a validation problem for invalid input. A resubmission
returns 200 on purpose: upstream systems retry on timeouts, and a retry shouldn't be an error or count
twice.

#### `GET /intermediaries/{intermediaryId}/statement?from=...&to=...`

```json
{
  "intermediaryId": "BRK-001",
  "from": "2026-11-01T00:00:00Z",
  "to": "2026-12-01T00:00:00Z",
  "totals": [{ "currency": "ZAR", "grossAmount": 18450.00, "activityCount": 12 }],
  "lastUpdated": "2026-11-01T14:02:11Z"
}
```

- The window includes `from` and excludes `to`, so back-to-back monthly statements never share an activity.
- Activities are placed by `occurredAt`, so a late arrival still lands in the period it belongs to.
- Totals are per currency; I don't add ZAR to USD or convert between them.
- `lastUpdated` shows how fresh the statement is (`null` for an empty window).

### Data model

```
activity
  source, external_id   text           primary key: one row per upstream activity
  version               bigint         latest version applied
  intermediary_id       text
  activity_type         text
  occurred_at           timestamptz    UTC
  amount                numeric(18,2)
  currency              text
  received_at           timestamptz    first seen
  updated_at            timestamptz    last newer version applied

index (intermediary_id, occurred_at)
```

- **Upstream identity as the primary key.** Postgres rejects a second row for the same activity, so
  duplicate detection is part of the insert, with no separate lookup that could race.
- **One row per activity, overwritten by newer versions.** The statement only needs current values and
  itemisation is optional, so I don't keep history. An append-only `activity_version` table could be
  added later without changing the API.
- **`numeric(18,2)` for money**, because floating point can't store most decimal amounts exactly.
- **The index matches the statement query:** one intermediary, a time range.

### Assumptions

- Upstream provides a stable ID and an increasing version per activity. Without a version there's no
  reliable way to tell which submission is newer, so I treat it as part of the integration contract.
- Corrections are new versions; reversals are new versions with amount 0 or negative.
- Same version, different payload: the first one stored wins. Flagging this as bad upstream data is a next step.
- `occurredAt` can be up to 5 minutes in the future (clock skew); backfills of any age are accepted.
- Ingestion is synchronous: when `POST` returns, the activity is in the statement. (With a queue in
  production it becomes asynchronous; see Task 3.)
- Only ZAR is accepted for now. Adding a currency is one value in the allowed list.
- I use the brief's term *intermediary* rather than *broker*, since brokers are one type of intermediary.

### How I guarantee correct totals

Every write goes through one SQL statement in `ActivityRepository.UpsertAsync`:

```sql
INSERT INTO activity (...) VALUES (...)
ON CONFLICT (source, external_id) DO UPDATE SET ...
WHERE activity.version < EXCLUDED.version
RETURNING (xmax = 0)
```

It tries to insert the activity. If one with the same `source` and `externalId` already exists, it
updates it, but only when the incoming version is higher. The check and the write happen together in
one statement, so nothing can slip in between them.

- **Duplicates.** The same activity sent again has the same version, so the `WHERE` is false and
  nothing changes. The API answers `ignored`, and the activity is still counted once.
- **Out of order.** If version 3 arrives before versions 1 and 2, the late ones fail the `WHERE`, so
  the stored row always holds the highest version, whatever order they arrived in.
- **Concurrent submissions.** Postgres locks the key while the statement runs. A second request for
  the same activity waits, then goes through the conflict path. Different activities for the same
  intermediary are different rows, so they don't block each other. There's no locking in the app.
- **The statement** sums the current row of each activity in the window, grouped by currency, so it
  always reflects the latest version of everything ingested so far.

This is the only raw SQL in the project. EF Core can't say "update only if newer" in one step, and
the obvious EF approach (read, then save) races: two requests can both see "not there yet" and both
insert. The SQL is Postgres-specific, which is why it lives in one method; switching databases would
mean rewriting that method, not the application.

Integration tests run against a real Postgres container and prove each rule:

| Rule | Test |
|---|---|
| Duplicate counted once | `PostActivity_SameActivitySubmittedTwice_CountedOnce` |
| Late older version ignored | `PostActivity_OlderVersionArrivesLate_DoesNotOverwriteNewer` |
| Newer version replaces the amount | `PostActivity_NewerVersion_ReplacesStoredAmount` |
| 50 concurrent submissions add up | `PostActivity_ConcurrentSubmissionsForSameIntermediary_TotalIsCorrect` |
| 20 versions at once, highest wins | `PostActivity_ConcurrentVersionsOfSameActivity_HighestVersionWins` |
| Window includes start, excludes end | `GetStatement_ActivitiesOnWindowBoundaries_IncludesStartExcludesEnd` |
| Invalid input rejected | `PostActivity_InvalidCurrency_ReturnsBadRequest` |

### Growth to more activity types and sources

- **A new source** needs no change. `source` is part of the key, so its IDs can't clash with other
  systems' IDs.
- **A new activity type** needs no change. It's stored as given and counted in the gross total. If
  statements need totals per type, that's one more `GROUP BY` column.
- **A new currency** is one more value in the allowed list. The statement already groups by currency.
- **A new delivery channel**, such as a queue, calls the same upsert, so the correctness rules come
  with it (see Task 3).
- **Audit history**, if needed, would be an append-only `activity_version` table written alongside the
  upsert, without changing the API.
- **Heavier statement reads** would first move to a read replica, and only then to pre-computed totals.

---

## Task 2: Observability

The brief's test is whether an operator can answer "Is it working? How fast? What broke?". These
are the signals I expose and the question each answers:

| Question | Signal | Where to see it |
|---|---|---|
| Is it working? | `/health/live` and `/health/ready`, and `created` / `updated` rising | health endpoints, `GET /metrics` |
| How fast? | `lastDelaySeconds`: time from `occurredAt` until the activity was saved | `GET /metrics`, and on every activity log line |
| What broke? | `failed` count, `/health/ready` turning unhealthy, error logs with a `TraceId` | `GET /metrics`, health endpoint, console logs |

**Structured logs.** Logs are JSON on the console. Every line carries the `TraceId` that ASP.NET Core
assigns to each request, or takes from an incoming W3C `traceparent` header, so one request can be
followed across services. Validation error responses include it as `traceId`, so a caller can quote it. I used
this built-in standard instead of writing a custom correlation header. Each activity gets one log
line with its outcome and delay, for example
`Activity life-admin/C-1001 v1 for BRK-001: Created (12s after it occurred)`.

**Metrics.** `GET /metrics` returns plain JSON:

```json
{ "created": 52, "updated": 19, "ignored": 6, "failed": 0, "lastDelaySeconds": 12 }
```

- `created`, `updated` and `ignored` show throughput. A jump in `ignored` usually means an upstream
  system is retrying in a loop.
- `failed` counts saves that crashed (database down, or a bug). The caller gets a 500 and the activity
  isn't stored, so this is the number that matters most.
- `lastDelaySeconds` shows how late activities are arriving. A growing value means statements are
  falling behind, usually because upstream is late.

**Health checks.** `/health/live` checks nothing beyond the process running, so a database outage
doesn't make the platform restart every instance in a loop. `/health/ready` checks the database with
a 2-second timeout, so the load balancer stops sending traffic to an instance that can't do useful
work, and resumes when it recovers.

**Two alerts I'd configure in CloudWatch.**

| Alert | Rough threshold | What it means | Operator action |
|---|---|---|---|
| Rising error rate | 5xx responses above 2% of requests for 5 minutes | Our service or database is broken | Check `/health/ready` and the RDS status, search the logs by `TraceId` for the failing requests, roll back if it started after a deploy |
| Growing delay | Processing delay p95 above 5 minutes for 10 minutes | We're healthy but activities arrive late, so statements are incomplete | Check the SQS queue depth and the upstream systems, and warn statement consumers if it persists |

**Limits I accepted.** The counters live in memory, are per instance and reset on restart, and I
keep the last delay rather than percentiles. That's enough for local viewing. In production the
service would publish the same numbers to CloudWatch Metrics (for example through OpenTelemetry), so
they're combined across instances and the delay alert can use p95. Observability isn't covered by
automated tests: the counters are trivial, so I verify them by hand and in the demo, and keep the
automated tests for the correctness rules.

## Task 3: AWS run plan

My hosting experience is mainly with Azure rather than AWS. In practice my first step would be to
follow whatever hosting platform and conventions the team already uses. Below is how I'd run this
service, described in Azure terms I know and mapped to the AWS equivalents.

| Concern | What I'd use on Azure | AWS equivalent |
|---|---|---|
| Compute | Container Apps (or AKS if the team already runs Kubernetes) | ECS on Fargate (or EKS) |
| Incoming activities | Service Bus | SQS, with a dead-letter queue |
| Database | Azure Database for PostgreSQL | RDS for PostgreSQL (Multi-AZ) |
| Secrets | Key Vault | Secrets Manager |
| Container images | Container Registry | ECR |
| Monitoring and alerts | Azure Monitor | CloudWatch |

![AWS architecture](docs/architecture.svg)

**Compute.** I'd run the service on ECS with Fargate, which runs containers without a cluster to
manage. For a single service that's simpler and cheaper than Kubernetes. If the organisation
already runs Kubernetes, I'd use EKS instead to stay consistent with the platform. ECS keeps at least
two copies (tasks) running, spread across Availability Zones behind an Application Load Balancer.
The load balancer only sends traffic to tasks whose `/health/ready` check passes, and ECS replaces
tasks whose `/health/live` check fails. If one zone goes down, the tasks in the other zones keep
serving.

**Ingestion.** In production I'd replace direct HTTP ingestion with an SQS queue. With HTTP, an
activity sent while the service or database is down is lost unless the sender retries. With a queue,
messages wait until the service is healthy again and are then processed. Messages that keep failing
go to a dead-letter queue for investigation. The consumer can be a small worker using the AWS SDK, or
a messaging library such as MassTransit (subject to its licence terms), and it calls the same upsert
as the HTTP endpoint. SQS can deliver a message more than once, which is safe because the upsert
ignores duplicates and older versions. Ingestion becomes asynchronous, so an activity reaches the
statement a few seconds after it's queued rather than immediately; the delay metric from Task 2
measures exactly that.

**Database.** A managed PostgreSQL instance (RDS, Multi-AZ). AWS handles patching, backups and
failover to a standby in another zone, and access is restricted to the service's network.

**Releases and migrations.**
- One Docker image is built once, tested in dev, and promoted unchanged through each environment to
  production.
- CI runs the build, the integration tests and smoke tests on every change.
- Database migrations run as a separate pipeline step before the new version is deployed. Migrations
  are written to be backward compatible, so the old and new versions can both run while tasks are
  replaced. Startup migrations are only switched on in development.
- Deployments are rolling: new tasks must pass their health checks before old ones stop, and the ECS
  deployment circuit breaker rolls back a release that keeps failing.

**Configuration and secrets.** Settings come from environment variables per environment. The database
connection string and other secrets live in Secrets Manager, never in the image or the repository.

**Common failure modes.**
- Database down: `/health/ready` fails, so tasks leave the load balancer, while activities wait in the
  queue and are processed when the database is back.
- A bad release: health checks fail during the rollout, so it stops and is rolled back.
- Upstream sending late: the delay metric rises and the delay alert fires (see Task 2).

**Capacity.** Peaks of a few thousand activities per hour average around one per second, which a
single task handles easily. I'd run at least two tasks for availability, with ECS Service Auto
Scaling on CPU and on SQS queue depth, so bursts are absorbed by the queue rather than dropped. The
database's connection limit is the main constraint as tasks are added, so I'd keep each task's
connection pool small.

## Trade-offs and what I chose not to build

- **Totals are calculated on read, not stored.** At this volume the indexed query is fast, and there's
  only one place a total can go wrong. A pre-computed totals table would make reads cheaper but adds a
  second copy of the truth to keep in step.
- **One row per activity, no history.** The statement only needs current values. Audit history can be
  added later as a separate table.
- **One Postgres-specific statement.** I accepted it in exchange for correctness without locks, and kept
  it in a single repository method.
- **Synchronous HTTP ingestion locally.** The brief asks for one ingestion mechanism; in production I'd
  put a queue in front (Task 3), which makes ingestion asynchronous.
- **ZAR only.** The model and statement already support several currencies; accepting another is a
  one-line change.
- **Same version, different payload isn't flagged.** The first one stored wins. Detecting and alerting
  on it is a next step.
- **Simple metrics.** In-memory counters and the last delay instead of a metrics library and
  percentiles.
- **No custom correlation header.** The built-in W3C `TraceId` already does the job.
- **No authentication.** Out of scope for the exercise; in production the API would sit behind the
  organisation's identity provider.
- **Tests focus on correctness.** The rules that could produce wrong totals are tested against a real
  database; observability is checked by hand.
- **Migrations on startup only in development.** In other environments they run as a pipeline step.

## How I used AI

See [PROMPTS.md](PROMPTS.md).
