# API Sentinel

API Sentinel is a developer workspace for saving HTTP APIs, executing requests, defining deterministic response checks, organizing collections, scheduling monitored tests, and reviewing failures and reliability reports. Groq provides optional, backend-only test suggestions and analysis; its output never determines whether a test passes.

## Features

- ASP.NET Core 8 API with 15-minute JWT access tokens, rotating HttpOnly refresh cookies, server-side token invalidation, seeded User/Admin roles, account controls, and user-scoped resources. User passwords are hashed with ASP.NET Core's password hasher; no default account password is stored in the source. Role IDs are fixed seed identifiers, not credentials.
- SQL Server persistence and startup EF Core migrations.
- GET, POST, PUT, PATCH, and DELETE request execution with configurable 1–120 second timeout, response-size cap, redirect refusal, and outbound-address protections.
- Searchable, paginated saved API definitions with encrypted Bearer, Basic, or API key credentials.
- User-owned environments with encrypted secret variables and `{{NAME}}` substitution in requests, query parameters, collections, and monitors.
- Test cases with expected status, response-time limits, JSON path assertions, and deterministic JSON response schemas.
- Collections with JSON path response extraction and chained `{{NAME}}` variables, execution history, Hangfire monitoring, transition alerts, and SQL-backed reports.
- Groq test generation, failed-run analysis, API test recommendations, and summaries of recent execution outcomes.
- React, TypeScript, Vite, Axios, Chart.js, and a separate local API testing laboratory.

## Requirements

- .NET 8 SDK
- Node.js 20.19+ and npm
- SQL Server Express or SQL Server LocalDB

Configure local settings from the repository root:

```powershell
dotnet user-secrets set Jwt:Key "a-random-secret-at-least-32-characters-long" --project backend/ApiSentinel.Api
dotnet user-secrets set ConnectionStrings:Default "Server=.\SQLEXPRESS;Database=ApiSentinel;Trusted_Connection=True;TrustServerCertificate=True" --project backend/ApiSentinel.Api
dotnet user-secrets set GROQ_API_KEY "your-groq-api-key" --project backend/ApiSentinel.Api
dotnet user-secrets set Groq:Model "openai/gpt-oss-120b" --project backend/ApiSentinel.Api
```

Groq is optional. Without a key, normal API execution, tests, collections, monitoring, and reports still work. Never put backend secrets in a `VITE_*` variable. In production, configure SQL, JWT, Groq, and a persistent protected Data Protection key ring through managed secret storage. The default key ring is machine-local and must not be lost if encrypted API credentials need to remain decryptable.

## Container deployment

Docker Compose runs SQL Server Express, the API, the built frontend, and Caddy. Point a DNS name at the host and allow inbound TCP 80/443, then copy `.env.example` to `.env`, set `DOMAIN`, and replace the SQL password, JWT key, and bootstrap key with unique secrets. Use a SQL password without semicolons. Start the stack with `docker compose up --build -d`; Caddy provisions HTTPS for the configured domain. Data Protection keys, SQL data, and Caddy certificates persist in named volumes. SQL Server Express is limited to 10 GB per database; set `MSSQL_PID` to a properly licensed edition for larger deployments. Remove `ADMIN_BOOTSTRAP_KEY` after creating the first administrator. Never commit `.env` or share the Groq key.

## Administrator bootstrap

To enable the one-time administrator bootstrap, configure a private random key of at least 32 characters:

```powershell
dotnet user-secrets set Admin:BootstrapKey "a-separate-random-bootstrap-key-at-least-32-characters" --project backend/ApiSentinel.Api
```

Register the account that should become the first administrator, sign in, then call `POST /api/admin/bootstrap` with `Authorization: Bearer <access-token>` and JSON body `{"key":"<configured-bootstrap-key>"}`. This endpoint succeeds only while no administrator exists. It revokes that account's current tokens, so sign in again afterward. Administrators can then search accounts and assign User/Admin roles from the workspace. Store the bootstrap key in managed secret storage in deployed environments and remove it after first use.

## Run locally

Start the backend:

```powershell
$env:ASPNETCORE_URLS="http://localhost:5080"
dotnet run --project backend/ApiSentinel.Api
```

The API applies migrations at startup. Swagger is at `http://localhost:5080/swagger`.

Start the frontend in another terminal:

```powershell
npm.cmd install --prefix frontend
npm.cmd run dev --prefix frontend
```

Start the controlled laboratory when needed:

```powershell
$env:ASPNETCORE_URLS="http://localhost:5081"
dotnet run --project backend/ApiTestingLaboratory
```

The laboratory exposes predictable status, created, empty, delay, malformed JSON, pagination, authentication, response-header, large-response, and CRUD scenarios. Production blocks loopback and private addresses. Integration tests enable access only to the configured lab port under the `Testing` environment.

## Database and migrations

EF Core migrations are in `backend/ApiSentinel.Api/Migrations`; the current relational model is documented in [docs/erd.md](docs/erd.md). The API migrates the configured SQL Server database at startup. To create and apply a migration during development:

```powershell
dotnet ef migrations add MigrationName --project backend/ApiSentinel.Api
dotnet ef database update --project backend/ApiSentinel.Api
```

The integration test creates a uniquely named SQL Express database and drops only that test database afterward. It does not use or delete the development database.

## Verification

```powershell
dotnet build backend/ApiSentinel.slnx
npm.cmd run build --prefix frontend
npm.cmd run lint --prefix frontend
```

Install the Playwright Chromium browser once, then run all backend integration and browser tests:

```powershell
dotnet build backend/ApiSentinel.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File backend/ApiSentinel.Tests/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet test backend/ApiSentinel.slnx
```

The Playwright test starts Vite automatically when port 5173 is free. Its API boundary is mocked to exercise the main workspace flows; SQL integration tests separately verify real persistence, authentication, ownership, request execution against the controlled lab, and queued monitoring jobs.

## Documentation

- [Architecture and current scope](docs/architecture.md)
- [ERD and relationship notes](docs/erd.md)
- [Security notes and limitations](docs/security.md)
- [`.env.example`](.env.example) contains placeholders only. The backend uses .NET user-secrets locally.
