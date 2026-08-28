# PowerP Real-Time API — Client & Documentation

The **PowerP Real-Time API** serves real-time and historical time-series data from your
plants over a secure, multi-tenant HTTP API. This repository is both the **reference
documentation** and a ready-to-use **.NET client library** with **C# and Python
examples**.

- **Base URL:** `https://{tenant}.powerp.app/rt-api/api/` — your team is given a
  dedicated hostname (e.g. `acme.powerp.app`). Your credentials only work on it.
- **Auth:** OAuth2 Client Credentials (RFC 6749), 1-hour bearer tokens.
- **Two query surfaces:** **v2** (recommended) — ask by *meaning*; **v1** (deprecated) —
  ask by explicit index.

---

## Table of contents

1. [Quick start](#quick-start)
2. [Concepts](#concepts) — tenants, buckets, signals, selectors
3. [Authentication](#authentication)
4. [Discovering what you can query](#discovering-what-you-can-query)
5. [Querying data (v2)](#querying-data-v2)
6. [Response format](#response-format)
7. [Errors & retries](#errors--retries)
8. [Best practices](#best-practices)
9. [The OpenAPI schema](#the-openapi-schema)
10. [The .NET client library](#the-net-client-library)
11. [Samples](#samples)
12. [v1 reference (deprecated)](#v1-reference-deprecated)

---

## Quick start

Three calls: authenticate, discover, query.

```bash
BASE="https://acme.powerp.app/rt-api/api"     # your dedicated host
DB=123                                          # your bucket id (provided by PowerP)

# 1. Get a bearer token (valid 1 hour)
TOKEN=$(curl -s -X POST "$BASE/v1/auth/token" \
  -d grant_type=client_credentials \
  -d client_id=$POWERP_CLIENT_ID \
  -d client_secret=$POWERP_CLIENT_SECRET | jq -r .access_token)

# 2. Discover the selector vocabulary for your bucket
curl -s "$BASE/v2/databases/$DB/vocabulary" -H "Authorization: Bearer $TOKEN"

# 3. Query the latest value of every inverter's active power, in one call
curl -s -X POST "$BASE/v2/query/latest" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"databaseId":123,"selector":{"level":"inverter","signal":"active_power"}}'
```

---

## Concepts

| Term | Meaning |
|---|---|
| **Tenant** | Your organization — the isolation boundary. You only ever see your own data. |
| **Bucket** (`databaseId`) | A named data space for one site or dataset. You query within a bucket. |
| **Signal** | One measured series (a meter reading, an inverter value, a status word). |
| **Selector** | A set of **tags** describing the signals you want, e.g. `{"level":"inverter","signal":"active_power"}`. The server resolves it to signals and runs the cheapest query. |

**Why selectors (v2) instead of indexes (v1):** with v1 you list explicit signal
indexes, up to 20 per call, and stitch results together. With v2 you describe *what* you
want and get a whole site — thousands of signals — in **one request**. New integrations
should use v2.

---

## Authentication

OAuth2 **Client Credentials** flow. Send your `client_id` and `client_secret` as
`application/x-www-form-urlencoded`:

```
POST /v1/auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=...&client_secret=...
```

```json
{ "access_token": "eyJ...", "expires_in": 3600, "token_type": "Bearer" }
```

Send the token on every request as `Authorization: Bearer <access_token>`. It is valid
for **1 hour** — reuse it, don't request one per call.

> **🔒 Host binding.** Your credentials are bound to your dedicated hostname. A token
> request or query sent to a different tenant's host is rejected (`401`/`403`), even with
> a valid secret. Always use the base URL PowerP gave you.

---

## Discovering what you can query

A bucket's **vocabulary** lists every selector dimension and the values it takes. Call it
first; build selectors from what it returns.

```
GET /v2/databases/{databaseId}/vocabulary
Authorization: Bearer <token>
```

```json
{
  "databaseId": 123,
  "bucket": "acme-site1",
  "signals": 502,
  "dimensions": {
    "site":   ["SITE1"],
    "level":  ["inverter", "meter", "relay"],
    "device": ["inv1", "inv2", "inv3", "meter1"],
    "signal": ["active_power", "current_a", "voltage_ab", "frequency", "..."],
    "class":  ["analog", "digital"]
  }
}
```

Any combination of these tags is a valid selector.

---

## Querying data (v2)

### Range query — `POST /v2/query`

```json
{
  "databaseId": 123,
  "selector": { "level": "inverter", "signal": "active_power" },
  "startTime": "2026-01-16T10:00:00Z",
  "endTime":   "2026-01-16T11:00:00Z",
  "resampleEvery": "1m",   // optional: aggregate into 1-minute windows; omit for raw points
  "explain": false          // optional: true returns the plan only, without moving data
}
```

- **`selector`** — the tags to match. An empty selector `{}` matches the whole bucket.
- **`resampleEvery`** — a window like `1m`, `5m`, `1h`. Each signal is aggregated with the
  aggregation that suits it (a counter is summed, a measurement is averaged). Omit for raw
  points. `windowPeriod` is accepted as an older spelling of the same field.
- **`maxDataPoints`** — instead of choosing a window, say how many points you want per
  series and the server derives the window that fits. The response tells you which one it
  used. Ignored when `resampleEvery` is given.
- **`minInterval`** — a floor for a derived window, e.g. `"1s"`. Set it to your source's
  real cadence so no window claims resolution the instrument never produced.
- **`aggFunction`** — `mean`, `last`, `max`, `sum`… Omit and each signal uses the
  aggregation declared for it in the catalogue.
- **`raw`** — `true` asks for raw points explicitly. Same result as omitting the window,
  but it says so out loud; combining it with `resampleEvery` is a `400` rather than a guess.
- **`explain`** — set `true` to see how many series a selector resolves to *before*
  running it. Use it to size a broad selector.
- **`decode`** — set `true` to expand status/bit-field signals into named conditions.

#### Raw or aggregated

**With no `resampleEvery` and no `maxDataPoints` you get raw points**: the values as they
were recorded, each keeping the instant it was measured at.

That default exists because aggregation is the lossy choice. Aggregated points are stamped
on window boundaries, not on the instants the values occurred at, so a state change, an
alarm or a setpoint move is reported at the edge of its window — earlier than it happened,
and always in the same direction. For anything where *when* is part of the datum, use raw.

Raw over a wide range is expensive, so it is **bounded and refused rather than silently
downgraded**: past the limit you get a `400` telling you the maximum, not a quietly
aggregated series that looks raw. Current limits are **3 h** of range and **500 signals**
per raw request. Beyond either, narrow the request or pass `resampleEvery` /
`maxDataPoints` and aggregate on purpose.

### Latest value — `POST /v2/query/latest`

The most recent value of every series a selector resolves to — the polling pattern, in
one call. No time window needed.

```json
{ "databaseId": 123, "selector": { "level": "inverter", "signal": "active_power" } }
```

### Sizing with `explain`

```json
{ "databaseId": 123, "selector": { "level": "inverter" }, "explain": true }
```

```json
{ "query": { "plan": "Batched", "seriesRequested": 168, "roundtrips": 2, "elapsedMs": 12 } }
```

---

## Response format

```json
{
  "query": {
    "plan": "Equalities",     // how the server resolved it: Equalities | Regex | Batched
    "roundtrips": 1,
    "seriesRequested": 3,
    "seriesReturned": 3,
    "elapsedMs": 43,
    "aggregated": false,      // did these points go through an aggregation window?
    "resampleEvery": null,    // the window actually applied; null when raw
    "aggFunction": null,      // what was applied, or "mixed"; null when raw
    "windowSource": "raw"     // raw | explicit | maxDataPoints | latest
  },
  "points": [
    {
      "streamKey": 8401,
      "tag": "inv1/active_power",
      "timestamp": "2026-01-16T10:00:30Z",
      "value": 312.5,
      "decoded": null           // present only for status signals queried with decode=true
    }
  ]
}
```

- **`points`** are **flat** — one object per point, not a series with nested points, and
  `streamKey` is a **number**. Each point carries its `tag` and `streamKey` so you can
  attribute it to a signal without a catalogue lookup. A range query returns many points
  per series; `latest` returns one.
- **`query`** is the execution report — useful for logging and for `explain`.
- **`query.aggregated`** is the field to check before you trust a timestamp. `false` means
  the instants are the real ones; `true` means they are window boundaries and
  `query.resampleEvery` tells you how wide. Do not infer this from what you asked for: the
  server may have derived a window you did not name.

> **Bind against the schema, not against this example.** The full request and response
> models are published as OpenAPI (see [The OpenAPI schema](#the-openapi-schema)).
> Generating your model is worth the five minutes: a hand-written one that mismatches
> deserialises to nulls without raising anything, and an empty result is indistinguishable
> from a plant that is genuinely idle.

---

## Errors & retries

Standard HTTP status codes; bodies are `application/problem+json`.

| Status | Meaning | What to do |
|---|---|---|
| `400` | Malformed request — bad selector, too many tags, invalid duration | Fix the request; do not retry as-is |
| `400` | Raw range or signal count over the limit | Narrow it, or pass `resampleEvery`/`maxDataPoints`. The message states the maximum |
| `401` | Missing/expired/invalid token, or **wrong host** | Re-authenticate; check you are using your own base URL |
| `403` | Authenticated but not allowed this resource | Check the `databaseId` belongs to you |
| `404` | Unknown bucket | Check the `databaseId` |
| `429` | Rate limit exceeded | **Back off** and retry (exponential) |
| `504` | The historian did not answer in time | Retry; narrow the time window or selector |

Implement **exponential backoff** for `429` and `5xx`. A `400`/`403`/`404` is a request
problem — fix it rather than retrying.

---

## Best practices

1. **Prefer v2 selectors.** One request per site instead of many. Use `explain` to size a
   broad selector before pulling it.
2. **Reuse the token** for its full hour; don't authenticate per call.
3. **Reuse one HTTP client** (or one `PowerPAPIClient`) for your process lifetime to avoid
   socket exhaustion.
4. **Poll with `/v2/query/latest`**, not a tight range query.
5. **Bound raw ranges.** Raw is the default and is capped; for long ranges pass
   `resampleEvery`, or `maxDataPoints` and let the server pick the window.
6. **Check `query.aggregated`** before treating a timestamp as an instant.
7. **Generate your models from the OpenAPI schema** rather than hand-writing them.
8. **Back off** on `429`/`5xx`.
9. **Protect your secret.** Environment variables or a secret manager — never in source
   control or logs. Use your dedicated host.

---

## The OpenAPI schema

The full contract — every endpoint, request and response model — is published as OpenAPI
3, and it is the authoritative description of the API. This document explains it; the
schema defines it.

```
https://<your-host>/rt-api/swagger/v2/swagger.json    # v2
https://<your-host>/rt-api/swagger/v1/swagger.json    # v1, deprecated
```

No authentication is needed to read it. Point your generator at it rather than
transcribing models by hand:

```bash
# example: a Python client
openapi-generator-cli generate -i https://<your-host>/rt-api/swagger/v2/swagger.json \
  -g python -o ./powerp-client
```

A hand-written model that does not match is the failure mode worth avoiding: most JSON
libraries bind unknown fields to nothing and missing fields to null without raising, so
the call returns `200`, the object is well-formed, and every value is empty. That looks
exactly like a site with no data.

---

## The .NET client library

`src/PowerP.Realtime.API.Client` (.NET 10) wraps auth, token refresh, and both query
surfaces.

```csharp
using PowerP.Realtime.API.Client;

var client = new PowerPAPIClient(
    baseUrl: "https://acme.powerp.app/rt-api/api/",
    clientId: Environment.GetEnvironmentVariable("POWERP_CLIENT_ID")!,
    clientSecret: Environment.GetEnvironmentVariable("POWERP_CLIENT_SECRET")!);

// Discover
var vocab = await client.GetVocabularyAsync(databaseId: 123);
Console.WriteLine(string.Join(", ", vocab.Dimensions["level"]));

// Latest value across all inverters, one call
var latest = await client.QuerySelectorLatestAsync(
    databaseId: 123,
    selector: new() { ["level"] = "inverter", ["signal"] = "active_power" });
foreach (var p in latest.Points)
    Console.WriteLine($"{p.Tag} = {p.Value} @ {p.Timestamp:o}");

// Range query, 1-minute resample
var series = await client.QuerySelectorAsync(
    databaseId: 123,
    selector: new() { ["level"] = "meter" },
    startTime: DateTime.UtcNow.AddHours(-1),
    endTime: DateTime.UtcNow,
    resampleEvery: "1m");   // or: maxDataPoints: 500, or neither for raw points
```

---

## Samples

- **`samples/python/query_v2.py`** — discover the vocabulary, run a selector range query,
  and poll latest values. Only `requests` is needed.
- **`samples/csharp/`** — a console app using the client library end to end.

Both read credentials from the environment:

```bash
export POWERP_API_BASE_URL="https://acme.powerp.app/rt-api/api/"
export POWERP_CLIENT_ID="<your-client-id>"
export POWERP_CLIENT_SECRET="<your-client-secret>"
```

```bash
python3 samples/python/query_v2.py
dotnet run --project samples/csharp/PowerP.Realtime.API.Sample.csproj
```

---

## v1 reference (deprecated)

> **Deprecated.** v1 still works and existing integrations are unaffected, but it is
> frozen and will be retired. v1 responses carry a `Deprecation: true` header. **Build new
> integrations on v2.**

v1 queries by explicit measurement index, **max 20 per call**.

- `POST /v1/query` — time-series by index, with optional `aggFunction` and `windowPeriod`.
  Body: `{ "databaseId", "measurementIndexes": ["1001","1002"], "startTime", "endTime", "aggFunction", "windowPeriod" }`
- `POST /v1/query/latest` — latest value per index. Body: `{ "databaseId", "measurementIndexes" }`
- `GET /v1/measurements` — list signals.

The v2 equivalents (`/v2/query`, `/v2/query/latest`, `/v2/.../vocabulary`) supersede all
three.

### Two v1 behaviours worth knowing

v1 is frozen, so these are documented rather than changed. Both are reasons to move to v2.

- **`windowPeriod` omitted does not mean raw beyond 30 minutes.** v1 returns raw points
  only when the range is **30 minutes or less**. Past that it aggregates automatically,
  choosing the window from the range — a 31-minute query comes back binned to 1 s, with
  nothing in the response saying so. v2 answers this by never aggregating unasked, and by
  reporting `aggregated` and `resampleEvery` on every response.
- **A non-numeric `measurementIndexes` entry is dropped silently.** Indexes that do not
  parse as integers are discarded without an error; if every entry is dropped the call
  returns `200` with an empty array. v2 selects by tags and reports what it resolved, so
  an empty result and a bad request are no longer the same response.
