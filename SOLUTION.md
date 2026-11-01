# IHub intermediary activity statement

## The problem

IHub turns activities from product systems (commissions, fees, adjustments) into a statement of what
each intermediary earned over a period. Summing amounts is easy. 

The ticky portion is the input: activities can arrive more than once, corrections can arrive before the original, and submissions for the same
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
- `amount` allows 2 decimal places and can be negative (clawbacks, reversals). `currency` is ISO 4217.

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
- Ingestion is synchronous: when `POST` returns, the activity is in the statement.

### How I'll guarantee correct totals

_To complete once the upsert and tests are done._ Plan: a single
`INSERT ... ON CONFLICT (source, external_id) DO UPDATE ... WHERE activity.version < EXCLUDED.version`,
so duplicates, out-of-order versions and concurrent writes are resolved in one atomic operation.

### Growth to more activity types and sources

_To be completed._

---

## Task 2: Observability

_To be completed._

## Task 3: AWS run plan

_To be completed._

## Trade-offs and what I chose not to build

_To be completed._

## How I used AI

See [PROMPTS.md](PROMPTS.md).
