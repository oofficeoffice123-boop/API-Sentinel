using System.Security.Claims;
using System.Text.RegularExpressions;
using ApiSentinel.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Api.Extensions;

public static partial class EnvironmentEndpointExtensions
{
    public static IEndpointRouteBuilder MapEnvironmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/environments").RequireAuthorization();
        group.MapGet("/", async (ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var environments = await db.Environments.Include(e => e.Variables).Where(e => e.UserId == userId).OrderBy(e => e.Name).ToListAsync(ct);
            return Results.Ok(environments.Select(e => new { e.Id, e.Name, e.IsActive, e.CreatedAt,
                variables = e.Variables.OrderBy(v => v.Key).Select(v => new { v.Id, v.Key, v.IsSecret, hasValue = v.IsSecret ? v.EncryptedValue != null : v.Value != null, value = v.IsSecret ? null : v.Value }) }));
        });
        group.MapPost("/", async (EnvironmentInput input, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
        {
            var userId = UserId(principal); var name = input.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return Results.BadRequest(new { error = "Environment name must be 1 to 100 characters." });
            if (await db.Environments.AnyAsync(e => e.UserId == userId && e.Name == name, ct)) return Results.Conflict(new { error = "An environment with this name already exists." });
            if (input.IsActive) await db.Environments.Where(e => e.UserId == userId && e.IsActive).ExecuteUpdateAsync(set => set.SetProperty(e => e.IsActive, false), ct);
            var environment = new ApiEnvironment { UserId = userId, Name = name, IsActive = input.IsActive, CreatedAt = DateTime.UtcNow };
            db.Environments.Add(environment); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/environments/{environment.Id}", new { environment.Id, environment.Name, environment.IsActive, environment.CreatedAt });
        });
        group.MapPut("/{id:int}", async (int id, EnvironmentInput input, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
        {
            var userId = UserId(principal); var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);
            if (environment is null) return Results.NotFound();
            var name = input.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return Results.BadRequest(new { error = "Environment name must be 1 to 100 characters." });
            if (await db.Environments.AnyAsync(e => e.UserId == userId && e.Id != id && e.Name == name, ct)) return Results.Conflict(new { error = "An environment with this name already exists." });
            if (input.IsActive) await db.Environments.Where(e => e.UserId == userId && e.Id != id && e.IsActive).ExecuteUpdateAsync(set => set.SetProperty(e => e.IsActive, false), ct);
            environment.Name = name; environment.IsActive = input.IsActive; await db.SaveChangesAsync(ct);
            return Results.Ok(new { environment.Id, environment.Name, environment.IsActive, environment.CreatedAt });
        });
        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
        {
            var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == id && e.UserId == UserId(principal), ct);
            if (environment is null) return Results.NotFound();
            db.Environments.Remove(environment); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        group.MapPut("/{id:int}/variables", async (int id, EnvironmentVariableInput input, ClaimsPrincipal principal, SentinelDb db, CredentialProtector protector, CancellationToken ct) =>
        {
            var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == id && e.UserId == UserId(principal), ct);
            if (environment is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Key) || !VariableKeyPattern().IsMatch(input.Key) || input.Value is null || input.Value.Length > 8192)
                return Results.BadRequest(new { error = "Use a valid variable key and a value no longer than 8192 characters." });
            var variable = await db.EnvironmentVariables.SingleOrDefaultAsync(v => v.EnvironmentId == id && v.Key == input.Key, ct);
            if (variable is null) { variable = new EnvironmentVariable { EnvironmentId = id, Key = input.Key }; db.EnvironmentVariables.Add(variable); }
            variable.IsSecret = input.IsSecret; variable.Value = input.IsSecret ? null : input.Value;
            variable.EncryptedValue = input.IsSecret ? protector.Protect(input.Value) : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { variable.Id, variable.Key, variable.IsSecret, hasValue = true, value = variable.IsSecret ? null : variable.Value });
        });
        group.MapDelete("/{id:int}/variables/{variableId:int}", async (int id, int variableId, ClaimsPrincipal principal, SentinelDb db, CancellationToken ct) =>
        {
            var variable = await db.EnvironmentVariables.SingleOrDefaultAsync(v => v.Id == variableId && v.EnvironmentId == id && v.Environment.UserId == UserId(principal), ct);
            if (variable is null) return Results.NotFound();
            db.EnvironmentVariables.Remove(variable); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        return endpoints;
    }

    private static int UserId(ClaimsPrincipal principal) => int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableKeyPattern();
}