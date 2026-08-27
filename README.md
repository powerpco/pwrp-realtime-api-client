# PowerP Real-Time API Client

**Date**: January 16, 2026

## Overview

This repository contains the client library and sample code for the **PowerP Real-Time API**. It provides a robust, reusable .NET library for consuming high-frequency measurement data, along with examples in C# and Python.

The client handles:
- **Authentication**: Implements the **Client Credentials Flow** (OAuth2), automatically acquiring and refreshing Bearer tokens.
- **Data Access**: Efficiently retrieves metadata and time-series data.
- **Two query surfaces**: **v1** by explicit measurement index (up to 20 per call), and **v2** by semantic *selector* — describe what you want by its tags and get a whole site in one request.
- **Optimization**: Demonstrates querying data in small blocks to ensure stability and performance.

---

## Repository Structure

- `src/PowerP.Realtime.API.Client/`: Reusable .NET 10.0 Class Library containing `PowerPAPIClient` and DTOs.
- `samples/csharp/`: Console application demonstrating batched queries.
- `samples/python/`: Jupyter notebook (`PowerPAPIClient.ipynb`) for Python integration.

---

## Quick Start (C#)

### 1. Prerequisites
- .NET 10.0 SDK
- Credentials provided by the PowerP Team.

### 2. Environment Setup
Configure your credentials as environment variables to keep them secure:

**Bash:**
```bash
export POWERP_CLIENT_ID="<your-client-id-guid>"
export POWERP_CLIENT_SECRET="<your-client-secret>"
# Optional: defaults to production if unset, or localhost for dev
export POWERP_API_BASE_URL="http://localhost:5000/api/" 
```

**PowerShell:**
```powershell
$env:POWERP_CLIENT_ID="<your-client-id-guid>"
$env:POWERP_CLIENT_SECRET="<your-client-secret>"
$env:POWERP_API_BASE_URL="http://localhost:5000/api/"
```

### 3. Running the Sample
```bash
dotnet run --project samples/csharp/PowerP.Realtime.API.Sample.csproj
```

---

## Production URL
For production environments, the Base URL follows this pattern:
**`https://{tenant}.powerp.app/rt-api/api/`**

Replace `{tenant}` with the tenant name assigned to you by the PowerP team (e.g., `acme`, `demo-hydro`).

---

## Best Practices
To ensure optimal performance and stability when consuming the API, please adhere to these guidelines:

1.  **Block Size (v1)**: On the v1 index path, request **5 to 10 signals per query** and never exceed 20 in a single request. This limit does **not** apply to the v2 selector query, which resolves and batches a whole site server-side — use it for broad reads, and call it with `explain: true` first to size the result.
2.  **Time Windows**: For raw data queries, keep the time window **under 30 minutes**. For larger ranges, perform multiple requests or use aggregated data.
3.  **Error Handling**:
    *   Validate HTTP responses (e.g., `response.EnsureSuccessStatusCode()`).
    *   Implement **Exponential Backoff** for `429 Too Many Requests` or `5xx Server Errors`.
4.  **Security**:
    *   **Never** commit credentials to source control. Use environment variables or secure vaults.
    *   Do not log full tokens or sensitive payloads.
5.  **Connection Pooling**: Reuse the `HttpClient` (or `PowerPAPIClient`) instance for the lifetime of your application to prevent socket exhaustion.

---

## API Reference

### 1. Authentication
**POST** `/api/v1/auth/token`
*   **Purpose**: Get a Bearer token (valid 1 hour).
*   **Content-Type**: `application/x-www-form-urlencoded`
*   **Body**: `client_id=...&client_secret=...&grant_type=client_credentials`
*   **Response**: `{ "access_token": "...", "expires_in": 3600, "token_type": "Bearer" }`

### 2. Metadata
**GET** `/api/v1/measurements`
*   **Purpose**: List all available signals/measurements.
*   **Headers**: `Authorization: Bearer <token>`

### 3. Data Query
**POST** `/api/v1/query`
*   **Purpose**: Get time-series values.
*   **Body**:
    ```json
    {
      "databaseId": 123,
      "measurementIndexes": ["1001", "1002"],
      "startTime": "2026-01-16T10:00:00Z",
      "endTime": "2026-01-16T10:15:00Z",
      "aggFunction": "mean", // or "last", "max", etc.
      "windowPeriod": "200ms" // Optional resampling window
    }
    ```

### 4. Latest Value
**POST** `/api/v1/query/latest`
*   **Purpose**: Get the most recent value for each measurement in a single call. Efficient and recommended for periodic polling (e.g. every few seconds/minutes).
*   **Body**:
    ```json
    {
      "databaseId": 123,
      "measurementIndexes": ["1001", "1002"]
    }
    ```
*   **Response**: `[ { "index": 1001, "timestamp": "...", "value": 12.3 }, ... ]`
*   **Notes**: Max 20 indexes per call. Optional `startTime`/`endTime` to bound the lookback window.

---

## API Reference — v2 (Selector Query)

v2 is additive: v1 stays exactly as documented above. The difference is how you ask for
data. Instead of enumerating measurement indexes (and staying under 20 per call), you
describe what you want by its **semantic tags** and the server resolves it to series and
compiles the cheapest query. **A whole site of thousands of signals is a single
request.**

### Selector Query
**POST** `/api/v2/query`
*   **Purpose**: Query by meaning. The `selector` is a set of tag dimensions the
    catalogue exposes for your tenant (e.g. `site`, `level`, `signal`). Ask the PowerP
    team for your bucket's vocabulary.
*   **Body**:
    ```json
    {
      "databaseId": 123,
      "selector": { "site": "SITE01", "level": "inverter", "signal": "active_power" },
      "startTime": "2026-01-16T10:00:00Z",
      "endTime": "2026-01-16T11:00:00Z",
      "resampleEvery": "1m",   // optional aggregation window; omit for raw points
      "explain": false          // true → return the plan only, do not execute
    }
    ```
*   **Response**:
    ```json
    {
      "query": {
        "plan": "Equalities",     // or "Regex" / "Batched"
        "roundtrips": 1,
        "seriesRequested": 78,
        "seriesReturned": 78,
        "elapsedMs": 43
      },
      "points": [
        { "streamKey": 1001, "tag": "SITE01/inverter-01/active_power",
          "timestamp": "2026-01-16T10:00:30Z", "value": 12.3, "decoded": null }
      ]
    }
    ```
*   **Notes**:
    *   An **empty selector** (`{}`) matches the whole bucket. Use `explain: true` first
        to see how many series it resolves to before pulling the data.
    *   Signals with a decode profile (a bit-field or status word) carry a `decoded`
        object alongside the raw `value`.

### Sizing a query with `explain`
Set `"explain": true` to get the `query` plan back **without moving any data**. It tells
you how many series the selector resolves to and how the server will run it — the way to
check a broad selector before committing to it.
