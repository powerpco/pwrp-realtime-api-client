# PowerP Real-Time API Client

**Date**: January 16, 2026

## Overview

This repository contains the client library and sample code for the **PowerP Real-Time API**. It provides a robust, reusable .NET library for consuming high-frequency measurement data, along with examples in C# and Python.

The client handles:
- **Authentication**: Implements the **Client Credentials Flow** (OAuth2), automatically acquiring and refreshing Bearer tokens.
- **Data Access**: Efficiently retrieves metadata and time-series data.
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

Replace `{tenant}` with your assigned tenant name (e.g., `hidroalto`, `termocandelaria`).

---

## Best Practices
To ensure optimal performance and stability when consuming the API, please adhere to these guidelines:

1.  **Block Size**: Request data for **5 to 10 signals per query**. Never exceed 20 signals in a single request, as this helps prevent timeouts and reduces load.
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
*   **Purpose**: Get a Bearer token.
*   **Body**: `{ "clientId": "...", "clientSecret": "..." }`
*   **Response**: `{ "accessToken": "...", "expiresIn": 3600 }`

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
