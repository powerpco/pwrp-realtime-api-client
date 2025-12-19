# Repository Guidelines

## Project Structure & Module Organization
- `src/PowerP.Realtime.API.Client/`: reusable .NET 8 client library (`PowerPAPIClient`, DTOs).
- `samples/csharp/`: console sample showing batched queries and grouping by database/aggregation.
- `samples/python/`: reference notebook (`PowerPAPIClient.ipynb`) and minimal `requirements.txt`.
- `PowerP.Realtime.API.Client.sln`: solution including library and sample; `bin/` and `obj/` are build outputs.

## Build, Test, and Development Commands
- `dotnet restore PowerP.Realtime.API.Client.sln` (first setup) to fetch SDK dependencies.
- `dotnet build PowerP.Realtime.API.Client.sln` to compile library and sample.
- `dotnet run --project samples/csharp/PowerP.Realtime.API.Sample.csproj` to execute the sample with `POWERP_API_KEY`/`POWERP_API_BASE_URL` set.
- `pip install -r samples/python/requirements.txt` then open `samples/python/PowerPAPIClient.ipynb` to run the Python example.
- `dotnet format` (if available) to apply consistent formatting before sending a PR.

## Coding Style & Naming Conventions
- 4-space indentation; keep implicit usings enabled per the project file.
- PascalCase for classes, methods, and properties; camelCase for locals and parameters; underscore-prefix private fields (e.g., `_httpClient`).
- DTOs should retain `JsonPropertyName` attributes matching the API schema; prefer nullable reference types where appropriate.
- Favor async/await with `Task` returns; reuse the shared `HttpClient` and call `EnsureSuccessStatusCode` on POST/GET responses.

## Testing Guidelines
- No automated tests exist yet. Add xUnit tests under a `PowerP.Realtime.API.Client.Tests/` project and name tests `ClassName_Method_ShouldExpectation`.
- Run `dotnet test` once tests are added; focus coverage on `PowerPAPIClient` success/error paths and time-window edge cases.

## API Configuration & Security Tips
- Do not commit real API keys. Prefer environment variables or user-secrets; for example:
  ```csharp
  var apiKey = Environment.GetEnvironmentVariable("POWERP_API_KEY");
  ```
- Respect API constraints (block size <= 20, lookback < 30 minutes) when adjusting `blockSize` or `lookbackMinutes` in `Program.cs`.
- Keep console logging informative but avoid writing sensitive tokens or tenant details.

## Commit & Pull Request Guidelines
- Use short, imperative commit messages (e.g., `add measurement query retry`), consistent with the existing history.
- PRs should include a clear description, linked issue/tenant context, commands executed (`dotnet build`, `dotnet run`), and sample console output when behavior changes.
- Keep changes scoped; prefer small, reviewable updates with DTO/schema diffs called out explicitly.
