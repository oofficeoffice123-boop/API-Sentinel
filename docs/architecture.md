# Architecture

The React client calls an ASP.NET Core API using a short-lived JWT kept in browser memory. A rotating HttpOnly cookie renews access tokens after reload; only hashes of refresh tokens are persisted in SQL Server. ASP.NET Core validates tokens and scopes API definition and run queries to the token subject. EF Core persists users, API definitions, and run snapshots to SQL Server. An `IHttpClientFactory` client executes target requests with redirects disabled, a timeout, a response-size cap, and DNS address checks. Groq is called only by the backend and is optional.

Service registrations live in `ApiSentinel.Api/Extensions/ServiceCollectionExtensions.cs`. `Program.cs` is the composition root and maps HTTP routes. The testing laboratory is a separate app and must not be deployed as the product API.

Run rows preserve URL and method snapshots so history remains readable after a saved API changes or is removed. See [ERD](erd.md). This is the implemented schema, not the future full product schema.