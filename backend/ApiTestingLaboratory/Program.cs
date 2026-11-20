using System.Text.Json;
using System.Collections.Concurrent;

var app = WebApplication.Create(args);
var resources = new ConcurrentDictionary<int, LabResource>();
var nextId = 0;
app.MapGet("/", () => Results.Ok(new { message = "API Sentinel testing laboratory" }));
app.MapGet("/status/{code:int}", (int code) => Results.Json(new { status = code }, statusCode: code));
app.MapPost("/created", () => Results.Created("/resource/1", new { id = 1 }));
app.MapMethods("/empty", ["GET"], () => Results.NoContent());
app.MapGet("/delay/{ms:int}", async (int ms, CancellationToken ct) => { await Task.Delay(Math.Clamp(ms, 0, 60_000), ct); return Results.Ok(new { delayedMs = Math.Clamp(ms, 0, 60_000) }); });
app.MapGet("/invalid-json", () => Results.Text("{invalid", "application/json"));
app.MapGet("/items", (int page = 1, int size = 10) => Results.Ok(new { page, size, items = Enumerable.Range(Math.Max(0, page - 1) * Math.Clamp(size, 1, 50), Math.Clamp(size, 1, 50)).Select(i => new { id = i + 1, name = $"Item {i + 1}" }) }));
app.MapGet("/auth", (HttpRequest req) => req.Headers.Authorization == "Bearer lab-token" ? Results.Ok(new { authenticated = true }) : Results.Unauthorized());
app.MapGet("/header-check", (HttpResponse response) => { response.Headers["X-Lab-Result"] = "ready"; response.Headers["X-Auth-Token"] = "lab-header-secret"; response.Headers.Append("Set-Cookie", "lab-session=secret; HttpOnly"); return Results.Ok(new { ok = true }); });
app.MapGet("/large", () => Results.Ok(new string('x', 1_100_000)));
app.MapGet("/resources", () => Results.Ok(resources.Values.OrderBy(x => x.Id)));
app.MapGet("/resources/{id:int}", (int id) => resources.TryGetValue(id, out var resource) ? Results.Ok(resource) : Results.NotFound());
app.MapPost("/resources", (LabResourceInput input) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) return Results.BadRequest(new { error = "Name is required and must be at most 120 characters." });
    var resource = new LabResource(Interlocked.Increment(ref nextId), input.Name.Trim(), input.Value);
    resources[resource.Id] = resource;
    return Results.Created($"/resources/{resource.Id}", resource);
});
app.MapPut("/resources/{id:int}", (int id, LabResourceInput input) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) return Results.BadRequest(new { error = "Name is required and must be at most 120 characters." });
    if (!resources.ContainsKey(id)) return Results.NotFound();
    var resource = new LabResource(id, input.Name.Trim(), input.Value);
    resources[id] = resource;
    return Results.Ok(resource);
});
app.MapPatch("/resources/{id:int}", (int id, LabResourcePatch input) =>
{
    if (!resources.TryGetValue(id, out var current)) return Results.NotFound();
    if (input.Name is { Length: > 120 } || input.Name is not null && string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest(new { error = "Name must be 1 to 120 characters." });
    var resource = current with { Name = input.Name?.Trim() ?? current.Name, Value = input.Value ?? current.Value };
    resources[id] = resource;
    return Results.Ok(resource);
});
app.MapDelete("/resources/{id:int}", (int id) => resources.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound());
app.Run();

record LabResource(int Id, string Name, int Value);
record LabResourceInput(string Name, int Value);
record LabResourcePatch(string? Name, int? Value);