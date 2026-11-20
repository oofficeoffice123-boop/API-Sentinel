using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ApiSentinel.Api;
using ApiSentinel.Api.Extensions;
using ApiSentinel.Api.Security;
using ApiSentinel.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Configure Jwt__Key with at least 32 random characters.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt__Key must contain at least 32 bytes.");
builder.Services.AddSentinelServices(builder.Configuration);
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<SentinelDb>().Database.MigrateAsync();
var forwardedHeaders = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    if (IPAddress.TryParse(proxy, out var address)) forwardedHeaders.KnownProxies.Add(address);
app.UseForwardedHeaders(forwardedHeaders);
const string RefreshCookieName = "api_sentinel_refresh";
app.UseSentinelErrors();
app.UseSwagger(); app.UseSwaggerUI(); app.UseRouting(); app.UseCors(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/auth/register", async (AuthInput input, HttpContext context, SentinelDb db, IPasswordHasher<User> hasher, CancellationToken ct) => {
    if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(input.Email, out _) || input.Password is null || input.Password.Length < 10 || input.Password.Length > 200) return Results.BadRequest(new { error = "Use a valid email and a password of 10 to 200 characters." });
    var email = input.Email.Trim().ToLowerInvariant(); if (await db.Users.AnyAsync(x => x.Email == email)) return Results.Conflict(new { error = "An account with that email already exists." });
    var user = new User { Email = email, Roles = [new UserRole { RoleId = 1 }] }; user.PasswordHash = hasher.HashPassword(user, input.Password); db.Users.Add(user); await db.SaveChangesAsync(ct); return await CreateSessionAsync(db, user, jwtKey, context, ct);
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/login", async (AuthInput input, HttpContext context, SentinelDb db, IPasswordHasher<User> hasher, CancellationToken ct) => {
    if (string.IsNullOrWhiteSpace(input.Email) || input.Password is null) return Results.Unauthorized();
    var user = await db.Users.Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Email == input.Email.Trim().ToLowerInvariant(), ct);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    return await CreateSessionAsync(db, user, jwtKey, context, ct);
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/refresh", async (HttpContext context, SentinelDb db, CancellationToken ct) => {
    var rawToken = context.Request.Cookies[RefreshCookieName];
    if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 256) { ExpireRefreshCookie(context); return Results.Unauthorized(); }
    var hash = HashRefreshToken(rawToken);
    var session = await db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTime.UtcNow || session.TokenVersion != session.User.TokenVersion) { ExpireRefreshCookie(context); return Results.Unauthorized(); }
    session.RevokedAt = DateTime.UtcNow;
    try { return await CreateSessionAsync(db, session.User, jwtKey, context, ct, session.Id); }
    catch (DbUpdateConcurrencyException) { return Results.Unauthorized(); }
}).RequireRateLimiting("auth");
var account = app.MapGroup("/api/account").RequireAuthorization();
account.MapGet("/profile", async Task<IResult> (ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
{
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var user = await db.Users.Include(u => u.Roles).ThenInclude(r => r.Role).SingleOrDefaultAsync(u => u.Id == userId, ct);
    return user is null ? Results.NotFound() : Results.Ok(new { user.Id, user.Email, roles = user.Roles.Select(r => r.Role.Name).ToArray() });
});
account.MapPut("/profile", async Task<IResult> (EmailChangeInput input, ClaimsPrincipal principal, SentinelDb db, IPasswordHasher<User> hasher, CancellationToken ct) =>
{
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return Results.NotFound();
    var email = input.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _)) return Results.BadRequest(new { error = "Enter a valid email address." });
    if (input.CurrentPassword is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed) return Results.BadRequest(new { error = "Current password is incorrect." });
    if (await db.Users.AnyAsync(u => u.Id != userId && u.Email == email, ct)) return Results.Conflict(new { error = "That email address is already in use." });
    user.Email = email; user.TokenVersion++; await db.SaveChangesAsync(ct);
    return Results.Ok(new { user.Id, user.Email });
});
account.MapPut("/password", async Task<IResult> (PasswordChangeInput input, ClaimsPrincipal principal, SentinelDb db, IPasswordHasher<User> hasher, CancellationToken ct) =>
{
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return Results.NotFound();
    if (input.CurrentPassword is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
        return Results.BadRequest(new { error = "Current password is incorrect." });
    if (input.NewPassword is null || input.NewPassword.Length is < 10 or > 200)
        return Results.BadRequest(new { error = "New password must be 10 to 200 characters." });
    user.PasswordHash = hasher.HashPassword(user, input.NewPassword);
    user.TokenVersion++;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
account.MapPost("/logout", async Task<IResult> (ClaimsPrincipal principal, HttpContext context, SentinelDb db, CancellationToken ct) =>
{
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return Results.NotFound();
    user.TokenVersion++;
    await db.SaveChangesAsync(ct);
    context.Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(context));
    return Results.NoContent();
});

var admin = app.MapGroup("/api/admin").RequireAuthorization();
admin.MapPost("/bootstrap", async Task<IResult> (ClaimsPrincipal principal, BootstrapInput input, IConfiguration config, SentinelDb db, CancellationToken ct) =>
{
    var expected = config["Admin:BootstrapKey"];
    if (string.IsNullOrWhiteSpace(expected) || Encoding.UTF8.GetByteCount(expected) < 32) return Results.NotFound();
    var providedBytes = Encoding.UTF8.GetBytes(input.Key ?? ""); var expectedBytes = Encoding.UTF8.GetBytes(expected);
    if (providedBytes.Length != expectedBytes.Length || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes)) return Results.Unauthorized();
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    if (await db.UserRoles.AnyAsync(r => r.RoleId == 2, ct)) return Results.Conflict(new { error = "An administrator is already configured." });
    var userId = UserId(principal); db.UserRoles.Add(new UserRole { UserId = userId, RoleId = 2 });
    var user = await db.Users.SingleAsync(u => u.Id == userId, ct); user.TokenVersion++;
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.Ok(new { message = "Administrator role assigned. Sign in again to activate it." });
}).RequireRateLimiting("auth");
admin.MapGet("/users", async (SentinelDb db, int page = 1, string? search = null, CancellationToken ct = default) =>
{
    page = Math.Max(1, page); var query = db.Users.AsQueryable();
    if (!string.IsNullOrWhiteSpace(search)) query = query.Where(u => u.Email.Contains(search));
    var total = await query.CountAsync(ct);
    var items = await query.OrderBy(u => u.Email).Skip((page - 1) * 20).Take(20).Select(u => new { u.Id, u.Email, roles = u.Roles.Select(r => r.Role.Name).ToArray() }).ToListAsync(ct);
    return Results.Ok(new { total, items });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));
admin.MapPut("/users/{id:int}/role", async Task<IResult> (int id, UserRoleInput input, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
{
    var roleName = input.Role?.Trim(); if (roleName is not ("User" or "Admin")) return Results.BadRequest(new { error = "Role must be User or Admin." });
    var currentId = UserId(principal); if (id == currentId) return Results.BadRequest(new { error = "You cannot change your own role." });
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct); if (user is null) return Results.NotFound();
    var isAdmin = user.Roles.Any(r => r.RoleId == 2);
    if (roleName == "Admin" && !isAdmin) db.UserRoles.Add(new UserRole { UserId = id, RoleId = 2 });
    else if (roleName == "User" && isAdmin)
    {
        if (!await db.UserRoles.AnyAsync(r => r.RoleId == 2 && r.UserId != id, ct)) return Results.Conflict(new { error = "At least one administrator must remain." });
        db.UserRoles.Remove(user.Roles.Single(r => r.RoleId == 2));
    }
    user.TokenVersion++; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    return Results.Ok(new { user.Id, user.Email, role = roleName });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

var api = app.MapGroup("/api/apis").RequireAuthorization();
var collections = app.MapGroup("/api/collections").RequireAuthorization();
collections.MapGet("/", async (ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p);
    return Results.Ok(await db.Collections.Where(c => c.UserId == uid).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.CreatedAt, RequestCount = c.Apis.Count }).ToListAsync());
});
collections.MapPost("/", async (CollectionInput x, ClaimsPrincipal p, SentinelDb db) => {
    var name = x.Name.Trim(); if (name.Length is 0 or > 120) return Results.BadRequest(new { error = "Collection name must be 1 to 120 characters." });
    var collection = new ApiCollection { UserId = UserId(p), Name = name, CreatedAt = DateTime.UtcNow }; db.Collections.Add(collection); await db.SaveChangesAsync();
    return Results.Created($"/api/collections/{collection.Id}", new { collection.Id, collection.Name, collection.CreatedAt });
});
collections.MapPut("/{id:int}", async (int id, CollectionInput input, ClaimsPrincipal p, SentinelDb db, CancellationToken ct) => {
    var uid = UserId(p); var collection = await db.Collections.SingleOrDefaultAsync(c => c.Id == id && c.UserId == uid, ct);
    if (collection is null) return Results.NotFound();
    var name = input.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return Results.BadRequest(new { error = "Collection name must be 1 to 120 characters." });
    if (await db.Collections.AnyAsync(c => c.UserId == uid && c.Id != id && c.Name == name, ct)) return Results.Conflict(new { error = "A collection with this name already exists." });
    collection.Name = name; await db.SaveChangesAsync(ct); return Results.Ok(new { collection.Id, collection.Name, collection.CreatedAt });
});
collections.MapDelete("/{id:int}", async (int id, ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p); var collection = await db.Collections.SingleOrDefaultAsync(c => c.Id == id && c.UserId == uid);
    if (collection is null) return Results.NotFound();
    await db.Apis.Where(a => a.CollectionId == id && a.UserId == uid).ExecuteUpdateAsync(set => set.SetProperty(a => a.CollectionId, (int?)null));
    db.Collections.Remove(collection); await db.SaveChangesAsync(); return Results.NoContent();
});
collections.MapPost("/{id:int}/run", async (int id, ClaimsPrincipal p, SentinelDb db, HttpRequestExecutor executor, CredentialProtector protector, EnvironmentResolver environmentResolver, CancellationToken ct) => {
    var uid = UserId(p);
            var collection = await db.Collections.Include(c => c.Apis).ThenInclude(a => a.Headers).Include(c => c.Apis).ThenInclude(a => a.QueryParameters).SingleOrDefaultAsync(c => c.Id == id && c.UserId == uid, ct);
    if (collection is null) return Results.NotFound();
    var variables = new Dictionary<string, EnvironmentValue>(await environmentResolver.GetActiveAsync(uid, ct), StringComparer.Ordinal);
    var results = new List<CollectionRunResult>();
    foreach (var request in collection.Apis.OrderBy(a => a.SortOrder).ThenBy(a => a.Id))
    {
        try
        {
            var credential = request.EncryptedCredential is null ? null : protector.Unprotect(request.EncryptedCredential);
            var response = await executor.ExecuteAsync(request.Url, request.Method, request.RequestBody, request.ContentType, SavedRequestHeaders(request, protector), request.AuthenticationType, credential, request.AuthHeaderName, variables, ct, request.TimeoutSeconds, SavedQueryParameters(request, protector));
            var extractionError = false;
            if (request.ExtractionJsonPath is not null && request.ExtractionVariableName is not null)
            {
                var extracted = AssertionEvaluator.ExtractJsonPath(response.Body, request.ExtractionJsonPath);
                if (extracted is null) extractionError = true;
                else variables[request.ExtractionVariableName] = new EnvironmentValue(extracted, true);
            }
            var passed = response.Status is >= 200 and < 300 && !extractionError;
            var error = extractionError ? "A collection response value could not be extracted." : !passed ? $"Expected a 2xx status; received {response.Status}." : null;
            var run = new TestRun { UserId = uid, Url = request.Url, Method = request.Method, Status = response.Status, DurationMs = response.DurationMs, Body = response.Body, Error = error ?? "", Passed = passed, CreatedAt = DateTime.UtcNow, ResponseHeaders = SavedResponseHeaders(response.Headers) };
            db.Runs.Add(run); await db.SaveChangesAsync(ct);
            results.Add(new(run.Id, request.Name, run.Status, run.DurationMs, run.Passed, error));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            var run = new TestRun { UserId = uid, Url = request.Url, Method = request.Method, Status = 0, Body = "", Error = e is TaskCanceledException ? "Request timed out." : "Request could not be completed.", Passed = false, CreatedAt = DateTime.UtcNow };
            db.Runs.Add(run); await db.SaveChangesAsync(ct);
            results.Add(new(run.Id, request.Name, run.Status, run.DurationMs, run.Passed, e is TaskCanceledException ? "Request timed out." : "Request could not be completed."));
        }
    }
    return Results.Ok(new { collection.Id, collection.Name, count = results.Count, passed = results.Count(r => r.Passed), results });
}).RequireRateLimiting("outbound");

api.MapGet("/", async (ClaimsPrincipal p, SentinelDb db, int page = 1, string? search = null, int? collectionId = null) => {
    var uid = UserId(p); page = Math.Max(page, 1); var q = db.Apis.Where(a => a.UserId == uid);
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Name.Contains(search) || a.Url.Contains(search));
    if (collectionId is not null) q = q.Where(a => a.CollectionId == collectionId);
    var items = await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).Skip((page - 1) * 20).Take(20).Select(a => new { a.Id, a.Name, a.Url, a.Method, a.RequestBody, a.ContentType, a.TimeoutSeconds, a.CollectionId, a.SortOrder, a.AuthenticationType, a.AuthHeaderName, a.ExtractionJsonPath, a.ExtractionVariableName, hasCredential = a.EncryptedCredential != null, headers = a.Headers.Select(h => new { h.Id, h.Name, h.IsSecret, value = h.IsSecret ? null : h.Value }), queryParameters = a.QueryParameters.OrderBy(qp => qp.SortOrder).Select(qp => new { qp.Id, qp.Name, qp.IsSecret, qp.IsEnabled, value = qp.IsSecret ? null : qp.Value }), a.CreatedAt }).ToListAsync();
    return Results.Ok(new { total = await q.CountAsync(), items });
});
api.MapPost("/", async (ApiInput x, ClaimsPrincipal p, SentinelDb db, CredentialProtector protector) => {
    var uid = UserId(p);
    if (!ValidApi(x.Name, x.Url, x.Method) || !ValidApiAuthentication(x, requireCredential: x.AuthenticationType != "None") || !ValidExtraction(x.ExtractionJsonPath, x.ExtractionVariableName) || !ValidRequestConfig(x) || !ValidQueryParameters(x.QueryParameters)) return Results.BadRequest(new { error = "Enter a name, supported method, URL, authentication settings, and valid request configuration." });
    if (x.CollectionId is not null && !await db.Collections.AnyAsync(c => c.Id == x.CollectionId && c.UserId == uid)) return Results.NotFound();
    var a = new ApiDefinition { UserId = uid, CollectionId = x.CollectionId, Name = x.Name.Trim(), Url = x.Url.Trim(), Method = x.Method.ToUpperInvariant(), RequestBody = x.RequestBody, ContentType = x.ContentType, TimeoutSeconds = x.TimeoutSeconds ?? 20, SortOrder = x.SortOrder, AuthenticationType = x.AuthenticationType, AuthHeaderName = x.AuthenticationType == "ApiKey" ? x.AuthHeaderName : null, EncryptedCredential = x.AuthenticationType == "None" || string.IsNullOrEmpty(x.Credential) ? null : protector.Protect(x.Credential), ExtractionJsonPath = x.ExtractionJsonPath, ExtractionVariableName = x.ExtractionVariableName, CreatedAt = DateTime.UtcNow };
    SaveRequestHeaders(a, x.Headers, protector);
    SaveQueryParameters(a, x.QueryParameters, protector);
    db.Apis.Add(a); await db.SaveChangesAsync(); return Results.Created($"/api/apis/{a.Id}", ApiDto(a));
});
api.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
{
    var userId = UserId(principal);
    var definition = await db.Apis.Include(a => a.Headers).Include(a => a.QueryParameters).SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    return definition is null ? Results.NotFound() : Results.Ok(ApiDto(definition));
});
api.MapPatch("/{id:int}/extraction", async (int id, ApiExtractionInput input, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
{
    var userId = UserId(principal);
    var definition = await db.Apis.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    if (definition is null) return Results.NotFound();
    if (!ValidExtraction(input.JsonPath, input.VariableName)) return Results.BadRequest(new { error = "Provide both a valid JSON path and runtime variable name, or clear both." });
    definition.ExtractionJsonPath = input.JsonPath; definition.ExtractionVariableName = input.VariableName;
    await db.SaveChangesAsync(ct); return Results.NoContent();
});
api.MapPost("/import-openapi", async (OpenApiImportInput input, ClaimsPrincipal principal, SentinelDb db, CredentialProtector protector, CancellationToken ct) => {
    var userId = UserId(principal);
    var specification = input.Specification;
    if (string.IsNullOrWhiteSpace(specification) || specification.Length > 1_000_000) return Results.BadRequest(new { error = "OpenAPI JSON or YAML must be between 1 byte and 1 MB." });
    if (!OpenApiJsonNormalizer.TryNormalizeYaml(specification, out specification)) return Results.BadRequest(new { error = "OpenAPI JSON or YAML is malformed or invalid." });
    if (input.CollectionId is not null && !await db.Collections.AnyAsync(c => c.Id == input.CollectionId && c.UserId == userId, ct)) return Results.NotFound();
    try
    {
        using var document = JsonDocument.Parse(specification, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("openapi", out _) || !root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "The document must be an OpenAPI JSON document with a paths object." });
        if (!root.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array || servers.GetArrayLength() == 0 ||
            servers[0].ValueKind != JsonValueKind.Object || !servers[0].TryGetProperty("url", out var serverField) || serverField.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(serverField.GetString(), UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            return Results.BadRequest(new { error = "Provide an absolute HTTP or HTTPS URL in the first servers entry." });
        var operations = new List<ApiDefinition>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var verbs = new HashSet<string>(["get", "post", "put", "patch", "delete"], StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.EnumerateObject())
        {
            if (path.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var operation in path.Value.EnumerateObject().Where(p => verbs.Contains(p.Name)))
            {
                if (operation.Value.ValueKind != JsonValueKind.Object) continue;
                var operationParameters = new List<JsonElement>();
                if (path.Value.TryGetProperty("parameters", out var pathParameters) && pathParameters.ValueKind == JsonValueKind.Array) operationParameters.AddRange(pathParameters.EnumerateArray());
                if (operation.Value.TryGetProperty("parameters", out var operationParams) && operationParams.ValueKind == JsonValueKind.Array) operationParameters.AddRange(operationParams.EnumerateArray());
                var urlPath = path.Name;
                var queryParameters = new List<ApiQueryParameterInput>();
                var requestHeaders = new List<ApiHeaderInput>();
                foreach (var parameter in operationParameters)
                {
                    if (parameter.ValueKind != JsonValueKind.Object || !parameter.TryGetProperty("name", out var parameterName) || parameterName.ValueKind != JsonValueKind.String || !parameter.TryGetProperty("in", out var parameterIn)) continue;
                    var paramName = parameterName.GetString()!;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(paramName, "^[A-Za-z_][A-Za-z0-9_.-]{0,63}$")) continue;
                    var template = "{{" + paramName.Replace('-', '_').Replace('.', '_') + "}}";
                    if (parameterIn.GetString() == "path") urlPath = urlPath.Replace("{" + paramName + "}", "{{" + paramName.Replace('-', '_').Replace('.', '_') + "}}", StringComparison.Ordinal);
                    else if (parameterIn.GetString() == "query") queryParameters.Add(new(paramName, template));
                    else if (parameterIn.GetString() == "header" && paramName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !paramName.Equals("Host", StringComparison.OrdinalIgnoreCase) && !paramName.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && !paramName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) requestHeaders.Add(new(paramName, template));
                    else if (parameterIn.GetString() == "header") unsupported.Add("restricted or invalid header parameter");
                }
                var url = baseUri.ToString().TrimEnd('/') + "/" + urlPath.TrimStart('/');
                if (url.Length > 2048) return Results.BadRequest(new { error = "An imported endpoint URL exceeds 2048 characters." });
                var name = operation.Value.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String ? summary.GetString() : null;
                name ??= operation.Value.TryGetProperty("operationId", out var operationId) && operationId.ValueKind == JsonValueKind.String ? operationId.GetString() : null;
                name = string.IsNullOrWhiteSpace(name) ? $"{operation.Name.ToUpperInvariant()} {path.Name}" : name.Trim();
                var authenticationType = "None";
                string? authHeaderName = null;
                JsonElement security = default;
                var hasSecurity = operation.Value.TryGetProperty("security", out security) || root.TryGetProperty("security", out security);
                if (hasSecurity && security.ValueKind != JsonValueKind.Array) unsupported.Add("invalid authentication requirement");
                else if (hasSecurity && security.ValueKind == JsonValueKind.Array && security.GetArrayLength() > 0)
                {
                    var requirement = security[0];
                    if (security.GetArrayLength() != 1 || requirement.ValueKind != JsonValueKind.Object || requirement.EnumerateObject().Count() != 1 ||
                        !root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object || !components.TryGetProperty("securitySchemes", out var schemes) || schemes.ValueKind != JsonValueKind.Object)
                        unsupported.Add("ambiguous or unresolved authentication scheme");
                    else
                    {
                        var schemeName = requirement.EnumerateObject().First().Name;
                        if (!schemes.TryGetProperty(schemeName, out var scheme) || scheme.ValueKind != JsonValueKind.Object || !scheme.TryGetProperty("type", out var schemeType) || schemeType.ValueKind != JsonValueKind.String)
                            unsupported.Add("unresolved authentication scheme");
                        else if (schemeType.GetString() == "http" && scheme.TryGetProperty("scheme", out var httpScheme) && httpScheme.ValueKind == JsonValueKind.String && httpScheme.GetString() is { } httpValue && httpValue.Equals("bearer", StringComparison.OrdinalIgnoreCase)) authenticationType = "Bearer";
                        else if (schemeType.GetString() == "http" && scheme.TryGetProperty("scheme", out httpScheme) && httpScheme.ValueKind == JsonValueKind.String && httpScheme.GetString() is { } basicValue && basicValue.Equals("basic", StringComparison.OrdinalIgnoreCase)) authenticationType = "Basic";
                        else if (schemeType.GetString() == "apiKey" && scheme.TryGetProperty("in", out var keyLocation) && keyLocation.GetString() == "header" && scheme.TryGetProperty("name", out var keyName) && keyName.ValueKind == JsonValueKind.String && keyName.GetString() is { } importedHeader && importedHeader.Length <= 100 && importedHeader.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !importedHeader.Equals("Host", StringComparison.OrdinalIgnoreCase) && !importedHeader.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            authenticationType = "ApiKey";
                            authHeaderName = importedHeader;
                        }
                        else unsupported.Add("unsupported authentication scheme");
                    }
                }
                string? requestBody = null;
                var contentType = "application/json";
                if (operation.Value.TryGetProperty("requestBody", out var bodyDefinition))
                {
                    if (bodyDefinition.ValueKind == JsonValueKind.Object && bodyDefinition.TryGetProperty("content", out var bodyContent) && bodyContent.ValueKind == JsonValueKind.Object && bodyContent.TryGetProperty("application/json", out var jsonContent))
                    {
                        if (jsonContent.TryGetProperty("example", out var example)) requestBody = example.GetRawText();
                        else if (jsonContent.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object && examples.EnumerateObject().FirstOrDefault().Value is { ValueKind: not JsonValueKind.Undefined } firstExample && firstExample.TryGetProperty("value", out var exampleValue)) requestBody = exampleValue.GetRawText();
                        else if (jsonContent.TryGetProperty("schema", out var schema)) requestBody = OpenApiExampleBuilder.Build(schema);
                    }
                    else unsupported.Add("non-JSON request body");
                }
                var definition = new ApiDefinition { UserId = userId, CollectionId = input.CollectionId, Name = name.Length > 120 ? name[..120] : name, Url = url, Method = operation.Name.ToUpperInvariant(), RequestBody = requestBody, ContentType = contentType, SortOrder = operations.Count, AuthenticationType = authenticationType, AuthHeaderName = authHeaderName, CreatedAt = DateTime.UtcNow };
                SaveRequestHeaders(definition, requestHeaders, protector);
                SaveQueryParameters(definition, queryParameters, protector);
                operations.Add(definition);
                if (operations.Count > 200) return Results.BadRequest(new { error = "A maximum of 200 operations can be imported at once." });
            }
        }
        if (operations.Count == 0) return Results.BadRequest(new { error = "No supported GET, POST, PUT, PATCH, or DELETE operations were found." });
        db.Apis.AddRange(operations); await db.SaveChangesAsync(ct);
        return Results.Ok(new { imported = operations.Count, unsupported = unsupported.ToArray(), items = operations.Select(ApiDto) });
    }
    catch (JsonException) { return Results.BadRequest(new { error = "OpenAPI JSON is malformed." }); }
}).RequireRateLimiting("outbound");
api.MapPut("/{id:int}", async (int id, ApiInput x, ClaimsPrincipal p, SentinelDb db, CredentialProtector protector) => {
    var uid = UserId(p); var a = await db.Apis.Include(a => a.Headers).Include(a => a.QueryParameters).SingleOrDefaultAsync(a => a.Id == id && a.UserId == uid);
    if (a is null) return Results.NotFound();
    if (!ValidApi(x.Name, x.Url, x.Method) || !ValidApiAuthentication(x, x.AuthHeaderName ?? a.AuthHeaderName, requireCredential: x.AuthenticationType != "None" && (x.AuthenticationType != a.AuthenticationType || a.EncryptedCredential is null)) || !ValidExtraction(x.ExtractionJsonPath, x.ExtractionVariableName) || !ValidRequestConfig(x) || !ValidQueryParameters(x.QueryParameters)) return Results.BadRequest(new { error = "Enter a name, supported method, URL, authentication settings, and valid request configuration." });
    if (x.CollectionId is not null && !await db.Collections.AnyAsync(c => c.Id == x.CollectionId && c.UserId == uid)) return Results.NotFound();
    a.Name = x.Name.Trim(); a.Url = x.Url.Trim(); a.Method = x.Method.ToUpperInvariant(); a.CollectionId = x.CollectionId; a.SortOrder = x.SortOrder; a.ExtractionJsonPath = x.ExtractionJsonPath; a.ExtractionVariableName = x.ExtractionVariableName; a.RequestBody = x.RequestBody; a.ContentType = x.ContentType; a.TimeoutSeconds = x.TimeoutSeconds ?? a.TimeoutSeconds;
    if (x.Headers is not null) { db.ApiRequestHeaders.RemoveRange(a.Headers); a.Headers.Clear(); SaveRequestHeaders(a, x.Headers, protector); }
    if (x.QueryParameters is not null) { var priorQueryParameters = a.QueryParameters.ToArray(); db.ApiQueryParameters.RemoveRange(priorQueryParameters); a.QueryParameters.Clear(); SaveQueryParameters(a, x.QueryParameters, protector, priorQueryParameters); }
    if (x.AuthenticationType == "None") { a.AuthenticationType = "None"; a.EncryptedCredential = null; a.AuthHeaderName = null; }
    else { a.AuthenticationType = x.AuthenticationType; a.AuthHeaderName = x.AuthenticationType == "ApiKey" ? x.AuthHeaderName ?? a.AuthHeaderName : null; if (!string.IsNullOrEmpty(x.Credential)) a.EncryptedCredential = protector.Protect(x.Credential); }
    await db.SaveChangesAsync(); return Results.Ok(ApiDto(a));
});
api.MapDelete("/{id:int}", async (int id, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p); var a = await db.Apis.SingleOrDefaultAsync(a => a.Id == id && a.UserId == uid);
    if (a is null) return Results.NotFound();
    var testIds = await db.TestCases.Where(t => t.ApiId == id).Select(t => t.Id).ToArrayAsync();
    var monitorIds = await db.Monitors.Where(m => testIds.Contains(m.TestCaseId)).Select(m => m.Id).ToArrayAsync();
    foreach (var monitorId in monitorIds) scheduler.Remove(monitorId);
    await db.MonitorHistory.Where(h => h.MonitorId != null && monitorIds.Contains(h.MonitorId.Value)).ExecuteUpdateAsync(set => set.SetProperty(h => h.MonitorId, (int?)null));
    await db.Monitors.Where(m => monitorIds.Contains(m.Id)).ExecuteDeleteAsync();
    await db.Runs.Where(r => r.TestCaseId != null && testIds.Contains(r.TestCaseId.Value)).ExecuteUpdateAsync(set => set.SetProperty(r => r.TestCaseId, (int?)null));
    db.Apis.Remove(a); await db.SaveChangesAsync(); return Results.NoContent();
});
api.MapGet("/{apiId:int}/tests", async (int apiId, ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p);
    if (!await db.Apis.AnyAsync(a => a.Id == apiId && a.UserId == uid)) return Results.NotFound();
    var tests = await db.TestCases.Where(t => t.ApiId == apiId).Select(t => new { t.Id, t.ApiId, t.Name, t.ExpectedStatus, t.MaxDurationMs, t.SchemaJson, Assertions = t.Assertions.Select(a => new { a.Id, a.JsonPath, a.Operator, a.ExpectedJson }) }).ToListAsync();
    return Results.Ok(tests);
});
app.MapGet("/api/test-cases", async (ClaimsPrincipal p, SentinelDb db, int page = 1) => {
    var uid = UserId(p); page = Math.Max(1, page);
    var q = db.TestCases.Where(t => t.Api.UserId == uid);
    var items = await q.OrderBy(t => t.Name).Skip((page - 1) * 20).Take(20).Select(t => new { t.Id, t.ApiId, t.Name, t.ExpectedStatus, t.MaxDurationMs, t.SchemaJson, Assertions = t.Assertions.Select(a => new { a.Id, a.JsonPath, a.Operator, a.ExpectedJson }) }).ToListAsync();
    return Results.Ok(new { total = await q.CountAsync(), items });
}).RequireAuthorization();
app.MapPost("/api/test-cases", async (TestCaseInput x, ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p); var apiDefinition = await db.Apis.SingleOrDefaultAsync(a => a.Id == x.ApiId && a.UserId == uid);
    if (apiDefinition is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 120 || x.MaxDurationMs is <= 0 || x.SchemaJson is { Length: > 32000 } || x.SchemaJson is not null && !JsonResponseSchemaValidator.IsValidDefinition(x.SchemaJson)) return Results.BadRequest(new { error = "Provide a name, valid response-time limit, and supported JSON schema." });
    var supported = new[] { "Equals", "NotEquals", "Contains", "DoesNotContain", "Exists", "DoesNotExist", "GreaterThan", "LessThan", "IsNull", "IsNotNull" };
    if (x.Assertions?.Any(a => string.IsNullOrWhiteSpace(a.JsonPath) || a.JsonPath.Length > 256 || !supported.Contains(a.Operator) || (a.Operator is not ("Exists" or "DoesNotExist" or "IsNull" or "IsNotNull") && a.Expected is null)) == true) return Results.BadRequest(new { error = "One or more assertions have an invalid path, operator, or expected value." });
    var test = new TestCase { ApiId = x.ApiId, Name = x.Name.Trim(), ExpectedStatus = x.ExpectedStatus, MaxDurationMs = x.MaxDurationMs, SchemaJson = x.SchemaJson,
        Assertions = (x.Assertions ?? []).Select(a => new TestAssertion { JsonPath = a.JsonPath, Operator = a.Operator, ExpectedJson = a.Expected?.GetRawText() }).ToList() };
    db.TestCases.Add(test); await db.SaveChangesAsync();
    return Results.Created($"/api/test-cases/{test.Id}", new { test.Id, test.ApiId, test.Name, test.ExpectedStatus, test.MaxDurationMs, test.SchemaJson, Assertions = test.Assertions.Select(a => new { a.Id, a.JsonPath, a.Operator, a.ExpectedJson }) });
}).RequireAuthorization();
app.MapDelete("/api/test-cases/{id:int}", async (int id, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p); var test = await db.TestCases.SingleOrDefaultAsync(t => t.Id == id && t.Api.UserId == uid);
    if (test is null) return Results.NotFound();
    var monitorIds = await db.Monitors.Where(m => m.TestCaseId == id).Select(m => m.Id).ToArrayAsync();
    foreach (var monitorId in monitorIds) scheduler.Remove(monitorId);
    await db.MonitorHistory.Where(h => h.MonitorId != null && monitorIds.Contains(h.MonitorId.Value)).ExecuteUpdateAsync(set => set.SetProperty(h => h.MonitorId, (int?)null));
    await db.Monitors.Where(m => monitorIds.Contains(m.Id)).ExecuteDeleteAsync();
    await db.Runs.Where(r => r.TestCaseId == id).ExecuteUpdateAsync(set => set.SetProperty(r => r.TestCaseId, (int?)null));
    db.TestCases.Remove(test); await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization();
app.MapGet("/api/runs", async (ClaimsPrincipal p, SentinelDb db, int page = 1) => {
    var uid = UserId(p); page = Math.Max(page, 1); var q = db.Runs.Where(r => r.UserId == uid);
    var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * 20).Take(20).Select(r => new {
        r.Id, r.TestCaseId, r.Url, r.Method, r.Status, r.DurationMs, r.Body, r.Error, r.Passed, r.CreatedAt,
        assertions = r.Assertions.Select(a => new { a.JsonPath, a.Operator, a.Actual, a.Expected, a.Passed, a.Error }),
        headers = r.ResponseHeaders.Select(h => new { h.Name, h.Value })
    }).ToListAsync();
    return Results.Ok(new { total = await q.CountAsync(), items });
}).RequireAuthorization();
var monitors = app.MapGroup("/api/monitors").RequireAuthorization();
monitors.MapGet("/", async (ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p);
    var items = await db.Monitors.Where(m => m.UserId == uid).OrderBy(m => m.Id).Select(m => new { m.Id, m.TestCaseId, TestName = m.TestCase.Name, ApiUrl = m.TestCase.Api.Url, m.FrequencyMinutes, m.Enabled, m.CreatedAt, LastCheck = db.MonitorHistory.Where(h => h.MonitorId == m.Id).OrderByDescending(h => h.CreatedAt).Select(h => new { h.Passed, h.Status, h.DurationMs, h.CreatedAt }).FirstOrDefault() }).ToListAsync();
    return Results.Ok(items);
});
monitors.MapPost("/", async Task<IResult> (MonitorInput x, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p);
    if (x.FrequencyMinutes is not (5 or 15 or 30 or 60 or 1440)) return Results.BadRequest(new { error = "Monitoring frequency must be 5, 15, 30, 60, or 1440 minutes." });
    var test = await db.TestCases.Include(t => t.Api).SingleOrDefaultAsync(t => t.Id == x.TestCaseId && t.Api.UserId == uid);
    if (test is null) return Results.NotFound();
    var monitor = new MonitorConfiguration { UserId = uid, TestCaseId = test.Id, FrequencyMinutes = x.FrequencyMinutes, Enabled = x.Enabled, CreatedAt = DateTime.UtcNow };
    db.Monitors.Add(monitor); await db.SaveChangesAsync(); scheduler.Apply(monitor.Id, monitor.FrequencyMinutes, monitor.Enabled);
    return Results.Created($"/api/monitors/{monitor.Id}", new { monitor.Id, monitor.TestCaseId, monitor.FrequencyMinutes, monitor.Enabled });
});
monitors.MapPut("/{id:int}", async Task<IResult> (int id, MonitorInput x, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p); var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.Id == id && m.UserId == uid);
    if (monitor is null) return Results.NotFound();
    if (x.FrequencyMinutes is not (5 or 15 or 30 or 60 or 1440)) return Results.BadRequest(new { error = "Monitoring frequency must be 5, 15, 30, 60, or 1440 minutes." });
    if (!await db.TestCases.AnyAsync(t => t.Id == x.TestCaseId && t.Api.UserId == uid)) return Results.NotFound();
    monitor.TestCaseId = x.TestCaseId; monitor.FrequencyMinutes = x.FrequencyMinutes; monitor.Enabled = x.Enabled;
    await db.SaveChangesAsync(); scheduler.Apply(monitor.Id, monitor.FrequencyMinutes, monitor.Enabled);
    return Results.Ok(new { monitor.Id, monitor.TestCaseId, monitor.FrequencyMinutes, monitor.Enabled });
});
monitors.MapDelete("/{id:int}", async (int id, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p); var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.Id == id && m.UserId == uid);
    if (monitor is null) return Results.NotFound(); scheduler.Remove(id);
    await db.MonitorHistory.Where(h => h.MonitorId == id).ExecuteUpdateAsync(set => set.SetProperty(h => h.MonitorId, (int?)null));
    db.Monitors.Remove(monitor); await db.SaveChangesAsync(); return Results.NoContent();
});
monitors.MapPost("/{id:int}/run-now", async (int id, ClaimsPrincipal p, SentinelDb db, MonitoringScheduler scheduler) => {
    var uid = UserId(p); if (!await db.Monitors.AnyAsync(m => m.Id == id && m.UserId == uid)) return Results.NotFound();
    return Results.Accepted(value: new { jobId = scheduler.Enqueue(id) });
});
monitors.MapGet("/{id:int}/history", async (int id, ClaimsPrincipal p, SentinelDb db, int page = 1) => {
    var uid = UserId(p); if (!await db.Monitors.AnyAsync(m => m.Id == id && m.UserId == uid)) return Results.NotFound();
    page = Math.Max(1, page); var q = db.MonitorHistory.Where(h => h.MonitorId == id);
    var items = await q.OrderByDescending(h => h.CreatedAt).Skip((page - 1) * 50).Take(50).Select(h => new MonitorHistoryDto(h.MonitorName, h.Url, h.Status, h.DurationMs, h.Passed, h.Error, h.CreatedAt)).ToListAsync();
    return Results.Ok(new { total = await q.CountAsync(), items });
});
app.MapGet("/api/alerts", async (ClaimsPrincipal p, SentinelDb db, int page = 1) => {
    var uid = UserId(p); page = Math.Max(1, page); var q = db.Alerts.Where(a => a.UserId == uid);
    return Results.Ok(new { unread = await q.CountAsync(a => !a.IsRead), total = await q.CountAsync(), items = await q.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * 50).Take(50).ToListAsync() });
}).RequireAuthorization();
app.MapPut("/api/alerts/{id:int}/read", async (int id, AlertReadInput x, ClaimsPrincipal p, SentinelDb db) => {
    var uid = UserId(p); var alert = await db.Alerts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == uid);
    if (alert is null) return Results.NotFound(); alert.IsRead = x.IsRead; await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/reports/dashboard", async (ClaimsPrincipal p, SentinelDb db, int days = 14) => {
    var uid = UserId(p); days = Math.Clamp(days, 1, 90); var since = DateTime.UtcNow.AddDays(-days);
    var runs = db.Runs.Where(r => r.UserId == uid && r.CreatedAt >= since);
    var totalRuns = await runs.CountAsync(); var passed = await runs.CountAsync(r => r.Passed);
    var daily = await runs.GroupBy(r => r.CreatedAt.Date).OrderBy(g => g.Key).Select(g => new { date = g.Key, count = g.Count(), passed = g.Count(r => r.Passed), averageMs = g.Average(r => (double)r.DurationMs) }).ToListAsync();
    var slowest = await runs.GroupBy(r => r.Url).Select(g => new { url = g.Key, averageMs = g.Average(r => (double)r.DurationMs), checks = g.Count(), failures = g.Count(r => !r.Passed) }).OrderByDescending(x => x.averageMs).Take(5).ToListAsync();
    var monitoringTotal = await db.MonitorHistory.CountAsync(h => h.CreatedAt >= since && h.Monitor != null && h.Monitor.UserId == uid);
    var monitoringPassed = await db.MonitorHistory.CountAsync(h => h.CreatedAt >= since && h.Monitor != null && h.Monitor.UserId == uid && h.Passed);
    var failures = await runs.Where(r => !r.Passed).OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new { r.Id, r.Url, r.Status, r.DurationMs, r.CreatedAt }).ToListAsync();
    var average = totalRuns == 0 ? 0 : await runs.AverageAsync(r => (double)r.DurationMs);
    return Results.Ok(new { days, apis = await db.Apis.CountAsync(a => a.UserId == uid), testCases = await db.TestCases.CountAsync(t => t.Api.UserId == uid), executions = totalRuns, passed, failed = totalRuns - passed, passRate = totalRuns == 0 ? 0 : Math.Round(100.0 * passed / totalRuns, 1), averageResponseMs = average, monitorAvailability = monitoringTotal == 0 ? 0 : Math.Round(100.0 * monitoringPassed / monitoringTotal, 1), daily, slowest, recentFailures = failures });
}).RequireAuthorization();
app.MapPost("/api/execute", async Task<IResult> (ExecuteInput x, ClaimsPrincipal p, SentinelDb db, HttpRequestExecutor executor, CredentialProtector protector, EnvironmentResolver environmentResolver, CancellationToken ct) => {
    var uid = UserId(p);
    TestCase? test = null;
    if (x.TestCaseId is not null)
    {
        test = await db.TestCases.Include(t => t.Assertions).Include(t => t.Api).ThenInclude(a => a.Headers).Include(t => t.Api).ThenInclude(a => a.QueryParameters).SingleOrDefaultAsync(t => t.Id == x.TestCaseId && t.Api.UserId == uid, ct);
        if (test is null) return Results.NotFound(new { error = "Test case not found." });
    }
    if (test is null && !ValidExecuteAuthentication(x)) return Results.BadRequest(new { error = "Authentication settings are invalid." });
    var url = test?.Api.Url ?? x.Url; var method = test?.Api.Method ?? x.Method;
    try
    {
        var variables = await environmentResolver.GetActiveAsync(uid, ct);
        var authType = test?.Api.AuthenticationType ?? x.AuthenticationType;
        var encrypted = test?.Api.EncryptedCredential;
        var credential = encrypted is null ? x.Credential : protector.Unprotect(encrypted);
        var headerName = test?.Api.AuthHeaderName ?? x.AuthHeaderName;
        var requestHeaders = test is null ? new Dictionary<string, string>() : SavedRequestHeaders(test.Api, protector);
        foreach (var header in x.Headers ?? []) requestHeaders[header.Key] = header.Value;
        var queryParameters = test is null ? x.QueryParameters ?? [] : SavedQueryParameters(test.Api, protector);
        if (test is not null && x.QueryParameters is not null) queryParameters.AddRange(x.QueryParameters);
        var requestBody = x.Body ?? test?.Api.RequestBody;
        var contentType = x.ContentType ?? test?.Api.ContentType;
        var timeoutSeconds = test?.Api.TimeoutSeconds ?? x.TimeoutSeconds ?? 20;
        if (timeoutSeconds is < 1 or > 120) return Results.BadRequest(new { error = "Request timeout must be between 1 and 120 seconds." });
        var response = await executor.ExecuteAsync(url, method, requestBody, contentType, requestHeaders, authType, credential, headerName, variables, ct, timeoutSeconds, queryParameters);
        var expectedStatus = test?.ExpectedStatus ?? x.ExpectedStatus;
        var assertions = (test?.Assertions ?? []).Select(a => AssertionEvaluator.Evaluate(response.Body, a, response.Headers)).ToArray();
        var schemaError = test?.SchemaJson is null ? null : JsonResponseSchemaValidator.Validate(response.Body, test.SchemaJson);
        var schemaPassed = schemaError is null;
        var statusPassed = expectedStatus is null ? response.Status is >= 200 and < 300 : response.Status == expectedStatus;
        var durationPassed = test?.MaxDurationMs is null || response.DurationMs <= test.MaxDurationMs;
        var passed = statusPassed && durationPassed && schemaPassed && assertions.All(a => a.Passed);
        var failureDetails = new List<string>();
        if (!statusPassed) failureDetails.Add(expectedStatus is null ? $"Expected a 2xx status; received {response.Status}." : $"Expected status {expectedStatus}; received {response.Status}.");
        if (!durationPassed) failureDetails.Add($"Response took {response.DurationMs} ms; maximum is {test!.MaxDurationMs} ms.");
        if (schemaError is not null) failureDetails.Add(schemaError);
        failureDetails.AddRange(assertions.Where(a => !a.Passed).Select(a => a.Error ?? $"Assertion {a.JsonPath} failed."));
        var run = new TestRun { UserId = uid, TestCaseId = test?.Id, Url = url, Method = method, Status = response.Status, DurationMs = response.DurationMs, Body = response.Body, Passed = passed, CreatedAt = DateTime.UtcNow,
            Error = string.Join(" ", failureDetails),
            Assertions = assertions.Select(a => new TestRunAssertion { JsonPath = a.JsonPath, Operator = a.Operator, Actual = Limit(a.Actual, 8192), Expected = Limit(a.Expected, 8192), Passed = a.Passed, Error = a.Error }).ToList(),
            ResponseHeaders = SavedResponseHeaders(response.Headers) };
        db.Runs.Add(run); await db.SaveChangesAsync(ct);
        return Results.Ok(new { run.Id, run.Status, run.DurationMs, run.Body, run.Passed, statusPassed, durationPassed, schemaPassed, schemaError, assertions, headers = response.Headers });
    }
    catch (TaskCanceledException) { var failed = await SaveFailedRunAsync(db, uid, test?.Id, url, method, "Request timed out.", ct); return Results.Json(new { error = failed.Error, runId = failed.Id, failed = true }, statusCode: 504); }
    catch (System.Security.Cryptography.CryptographicException) { return Results.Problem("Stored API credentials could not be decrypted.", statusCode: 500); }
    catch (ArgumentException e) { var failed = await SaveFailedRunAsync(db, uid, test?.Id, url, method, e.Message, ct); return Results.BadRequest(new { error = failed.Error, runId = failed.Id, failed = true }); }
    catch (InvalidOperationException e) { var failed = await SaveFailedRunAsync(db, uid, test?.Id, url, method, e.Message, ct); return Results.BadRequest(new { error = failed.Error, runId = failed.Id, failed = true }); }
    catch (HttpRequestException) { var failed = await SaveFailedRunAsync(db, uid, test?.Id, url, method, "Target API could not be reached.", ct); return Results.Json(new { error = failed.Error, runId = failed.Id, failed = true }, statusCode: 502); }
}).RequireAuthorization().RequireRateLimiting("outbound");

app.MapAiEndpoints();
app.MapEnvironmentEndpoints();

app.Run();

static int UserId(ClaimsPrincipal p) => int.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
static async Task<TestRun> SaveFailedRunAsync(SentinelDb db, int userId, int? testCaseId, string url, string method, string error, CancellationToken ct)
{
    var run = new TestRun { UserId = userId, TestCaseId = testCaseId, Url = url, Method = method, Status = 0, DurationMs = 0, Body = "", Error = error, Passed = false, CreatedAt = DateTime.UtcNow };
    db.Runs.Add(run); await db.SaveChangesAsync(ct); return run;
}
static bool ValidApi(string name, string url, string method)
{
    var candidate = System.Text.RegularExpressions.Regex.Replace(url, @"\{\{[A-Za-z_][A-Za-z0-9_]*\}\}", match => match.Index == 0 ? "https://api.example.test" : "api.example.test");
    return !string.IsNullOrWhiteSpace(name) && name.Length <= 120 && Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https") && string.IsNullOrEmpty(uri.UserInfo) && (method.ToUpperInvariant() is "GET" or "POST" or "PUT" or "PATCH" or "DELETE");
}
static bool ValidApiAuthentication(ApiInput input, string? existingHeader = null, bool requireCredential = true)
{
    if (input.AuthenticationType == "None") return string.IsNullOrEmpty(input.Credential) && string.IsNullOrEmpty(input.AuthHeaderName);
    if (input.AuthenticationType is not ("Bearer" or "Basic" or "ApiKey")) return false;
    var credential = input.Credential;
    if (credential is null && requireCredential) return false;
    if (credential is not null && (credential.Length > 4096 || credential.Any(char.IsControl) || input.AuthenticationType == "Basic" && !credential.Contains(':'))) return false;
    if (input.AuthenticationType != "ApiKey") return true;
    var header = input.AuthHeaderName ?? existingHeader;
    return !string.IsNullOrWhiteSpace(header) && header.Length <= 100 && header.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !header.Equals("Host", StringComparison.OrdinalIgnoreCase) && !header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
}
static bool ValidExecuteAuthentication(ExecuteInput input) => input.AuthenticationType switch
{
    "None" => true,
    "Bearer" => ValidCredential(input.Credential),
    "Basic" => ValidCredential(input.Credential) && input.Credential!.Contains(':'),
    "ApiKey" => ValidCredential(input.Credential) && !string.IsNullOrWhiteSpace(input.AuthHeaderName) && input.AuthHeaderName.Length <= 100 && input.AuthHeaderName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !input.AuthHeaderName.Equals("Host", StringComparison.OrdinalIgnoreCase) && !input.AuthHeaderName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase),
    _ => false
};
static bool ValidCredential(string? credential) => !string.IsNullOrEmpty(credential) && credential.Length <= 4096 && !credential.Any(char.IsControl);
static bool ValidExtraction(string? path, string? variable) => path is null && variable is null || !string.IsNullOrWhiteSpace(path) && path.Length <= 256 && !string.IsNullOrWhiteSpace(variable) && System.Text.RegularExpressions.Regex.IsMatch(variable, "^[A-Za-z_][A-Za-z0-9_]{0,63}$");
static bool ValidRequestConfig(ApiInput input)
{
    if (input.RequestBody?.Length > 1_000_000 || string.IsNullOrWhiteSpace(input.ContentType) || input.ContentType.Length > 100 || input.ContentType.Any(char.IsControl) || input.TimeoutSeconds is < 1 or > 120) return false;
    var headers = input.Headers ?? [];
    return headers.Count <= 50 && headers.Select(h => h.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == headers.Count && headers.All(h =>
        !string.IsNullOrWhiteSpace(h.Name) && h.Name.Length <= 100 && h.Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
        !h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase) && !h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
        !h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && h.Value is not null && h.Value.Length <= 8192 && !h.Value.Any(char.IsControl));
}
static bool ValidQueryParameters(List<ApiQueryParameterInput>? parameters) => parameters is null || parameters.Count <= 100 && parameters.All(parameter =>
    parameter is not null && !string.IsNullOrWhiteSpace(parameter.Name) && parameter.Name.Length <= 256 && !parameter.Name.Any(char.IsControl) && parameter.Value is not null && parameter.Value.Length <= 8192);
static void SaveRequestHeaders(ApiDefinition api, List<ApiHeaderInput>? inputs, CredentialProtector protector)
{
    foreach (var header in inputs ?? [])
    {
        var isSecret = header.IsSecret || SensitiveValueClassifier.IsSensitiveName(header.Name);
        api.Headers.Add(new ApiRequestHeader { ApiId = api.Id, Name = header.Name, IsSecret = isSecret, Value = isSecret ? null : header.Value, EncryptedValue = isSecret ? protector.Protect(header.Value) : null });
    }
}
static void SaveQueryParameters(ApiDefinition api, List<ApiQueryParameterInput>? inputs, CredentialProtector protector, IReadOnlyCollection<ApiQueryParameter>? existing = null)
{
    var order = 0;
    foreach (var parameter in inputs ?? [])
    {
        var isSecret = parameter.IsSecret || SensitiveValueClassifier.IsSensitiveName(parameter.Name);
        var previous = isSecret && string.IsNullOrEmpty(parameter.Value) ? existing?.FirstOrDefault(item => item.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase) && item.IsSecret) : null;
        api.QueryParameters.Add(new ApiQueryParameter { ApiId = api.Id, Name = parameter.Name, IsSecret = isSecret, Value = isSecret ? null : parameter.Value, EncryptedValue = previous?.EncryptedValue ?? (isSecret ? protector.Protect(parameter.Value) : null), IsEnabled = parameter.IsEnabled, SortOrder = order++ });
    }
}
static Dictionary<string, string> SavedRequestHeaders(ApiDefinition api, CredentialProtector protector) => api.Headers.ToDictionary(h => h.Name, h => h.IsSecret ? protector.Unprotect(h.EncryptedValue!) : h.Value ?? "", StringComparer.OrdinalIgnoreCase);
static List<ApiQueryParameterInput> SavedQueryParameters(ApiDefinition api, CredentialProtector protector) => api.QueryParameters.OrderBy(parameter => parameter.SortOrder)
    .Select(parameter => new ApiQueryParameterInput(parameter.Name, parameter.IsSecret ? protector.Unprotect(parameter.EncryptedValue!) : parameter.Value ?? "", parameter.IsSecret, parameter.IsEnabled)).ToList();
static List<TestRunResponseHeader> SavedResponseHeaders(Dictionary<string, string> headers) => headers
    .Where(header => !SensitiveValueClassifier.IsSensitiveName(header.Key))
    .Select(header => new TestRunResponseHeader { Name = header.Key, Value = Limit(header.Value, 8192) ?? "" }).ToList();
static string? Limit(string? value, int maximum) => value is null || value.Length <= maximum ? value : value[..maximum];
static object ApiDto(ApiDefinition api) => new { api.Id, api.Name, api.Url, api.Method, api.RequestBody, api.ContentType, api.TimeoutSeconds, api.CollectionId, api.SortOrder, api.AuthenticationType, api.AuthHeaderName, api.ExtractionJsonPath, api.ExtractionVariableName, hasCredential = api.EncryptedCredential != null, headers = api.Headers.Select(h => new { h.Id, h.Name, h.IsSecret, value = h.IsSecret ? null : h.Value }), queryParameters = api.QueryParameters.OrderBy(p => p.SortOrder).Select(p => new { p.Id, p.Name, p.IsSecret, p.IsEnabled, value = p.IsSecret ? null : p.Value }), api.CreatedAt };
static CookieOptions RefreshCookieOptions(HttpContext context) => new()
{
    HttpOnly = true,
    Secure = context.Request.IsHttps || (!context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && !context.RequestServices.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing")),
    SameSite = SameSiteMode.Strict,
    Path = "/api/auth"
};
static void ExpireRefreshCookie(HttpContext context) => context.Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(context));
static async Task<IResult> CreateSessionAsync(SentinelDb db, User user, string key, HttpContext context, CancellationToken ct, int? rotatingTokenId = null)
{
    if (!db.Entry(user).Collection(x => x.Roles).IsLoaded)
        await db.Entry(user).Collection(x => x.Roles).Query().Include(x => x.Role).LoadAsync(ct);
    var now = DateTime.UtcNow;
    var staleTokens = db.RefreshTokens.Where(x => x.UserId == user.Id && (x.RevokedAt != null || x.TokenVersion != user.TokenVersion));
    var expiredTokens = db.RefreshTokens.Where(x => x.UserId == user.Id && x.ExpiresAt <= now);
    if (rotatingTokenId is not null) { staleTokens = staleTokens.Where(x => x.Id != rotatingTokenId.Value); expiredTokens = expiredTokens.Where(x => x.Id != rotatingTokenId.Value); }
    await staleTokens.ExecuteDeleteAsync(ct);
    await expiredTokens.ExecuteDeleteAsync(ct);
    var refreshToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = HashRefreshToken(refreshToken), TokenVersion = user.TokenVersion, CreatedAt = now, ExpiresAt = now.AddDays(30) });
    await db.SaveChangesAsync(ct);
    if (rotatingTokenId is not null) await db.RefreshTokens.Where(x => x.Id == rotatingTokenId.Value).ExecuteDeleteAsync(ct);
    var cookieOptions = RefreshCookieOptions(context); cookieOptions.MaxAge = TimeSpan.FromDays(30);
    context.Response.Cookies.Append(RefreshCookieName, refreshToken, cookieOptions);
    return Results.Ok(new { token = CreateAccessToken(user, key), user = user.Email });
}
static string HashRefreshToken(string token) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
static string CreateAccessToken(User u, string key) { var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, u.Id.ToString()), new(ClaimTypes.Email, u.Email), new("token_version", u.TokenVersion.ToString()) }; claims.AddRange(u.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.Name))); var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256); var token = new JwtSecurityToken("api-sentinel", "api-sentinel-client", claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: creds); return new JwtSecurityTokenHandler().WriteToken(token); }
public partial class Program { }