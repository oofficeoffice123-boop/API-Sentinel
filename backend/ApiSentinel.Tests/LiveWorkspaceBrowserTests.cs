using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace ApiSentinel.Tests;

public sealed class LiveWorkspaceBrowserTests : PageTest
{
    private static readonly ConcurrentDictionary<int, ConcurrentQueue<string>> ProcessLogs = new();
    [Fact]
    public async Task Browser_can_register_run_and_persist_a_test_against_the_real_stack()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var database = $"ApiSentinel_Browser_{Guid.NewGuid():N}";
        var jwtKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        var apiPort = FreePort(); var labPort = FreePort(); var frontendPort = FreePort();
        var apiUrl = $"http://127.0.0.1:{apiPort}"; var labUrl = $"http://127.0.0.1:{labPort}"; var frontendUrl = $"http://127.0.0.1:{frontendPort}";
        var processes = new List<Process>();
        try
        {
            var lab = Start("dotnet", ["run", "--no-build", "--project", Path.Combine(root, "backend", "ApiTestingLaboratory"), "--urls", labUrl], root);
            processes.Add(lab); await WaitForHttpAsync($"{labUrl}/", lab);

            var api = Start("dotnet", ["run", "--no-build", "--project", Path.Combine(root, "backend", "ApiSentinel.Api"), "--urls", apiUrl], root, new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Testing",
                ["DOTNET_ENVIRONMENT"] = "Testing",
                ["ASPNETCORE_URLS"] = apiUrl,
                ["Jwt__Key"] = jwtKey,
                ["ConnectionStrings__Default"] = $"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True",
                ["Outbound__TestingLaboratoryPort"] = labPort.ToString(),
                ["FrontendOrigin"] = frontendUrl
            });
            processes.Add(api); await WaitForHttpAsync($"{apiUrl}/api/health", api);

            var frontendPath = Path.Combine(root, "frontend");
            var frontend = Start("node", [Path.Combine(frontendPath, "node_modules", "vite", "bin", "vite.js"), "--host", "127.0.0.1", "--port", frontendPort.ToString(), "--strictPort"], frontendPath,
                new Dictionary<string, string> { ["VITE_API_URL"] = $"{apiUrl}/api" });
            processes.Add(frontend); await WaitForHttpAsync(frontendUrl, frontend);

            await Page.GotoAsync(frontendUrl);
            await Page.GetByRole(AriaRole.Button, new() { Name = "New here? Create an account" }).ClickAsync();
            var email = $"live-{Guid.NewGuid():N}@example.test";
            await Page.GetByLabel("Email").FillAsync(email);
            await Page.GetByLabel("Password").FillAsync("Live-browser-secure-password!");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "API overview" })).ToBeVisibleAsync();
            Assert.Null(await Page.EvaluateAsync<string?>("localStorage.getItem('token')"));
            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "API overview" })).ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Link, new() { Name = "API workspace" }).ClickAsync();
            await Page.GetByLabel("Request URL").FillAsync($"{labUrl}/header-check");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Send request" }).ClickAsync();
            await Expect(Page.Locator(".responsehead").Filter(new() { HasText = "PASS" })).ToBeVisibleAsync();
            await Expect(Page.Locator(".response")).ToContainTextAsync("ready");

            await Page.GetByLabel("API name").FillAsync("Live laboratory check");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save request" }).ClickAsync();
            await Expect(Page.GetByText("Live laboratory check", new() { Exact = true })).ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Link, new() { Name = "Tests and collections" }).ClickAsync();
            await Page.GetByLabel("Collection name").FillAsync("Live suite");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("Live suite", new() { Exact = true })).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Link, new() { Name = "API workspace" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
            await Page.GetByLabel("Collection", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Live suite" });
            await Page.GetByRole(AriaRole.Button, new() { Name = "Update request" }).ClickAsync();
            await Page.GetByRole(AriaRole.Link, new() { Name = "Tests and collections" }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Run all" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Collection finished: 1 of 1 requests passed.");

            await Page.GetByLabel("API for test").SelectOptionAsync("1");
            await Page.GetByPlaceholder("Test name").FillAsync("Live response header check");
            await Page.GetByLabel("Assertion source").SelectOptionAsync("header");
            await Page.GetByLabel("Response header name").FillAsync("X-Lab-Result");
            await Page.GetByLabel("Assertion operator").SelectOptionAsync("Equals");
            await Page.GetByLabel("Expected value").FillAsync("ready");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save test case" }).ClickAsync();
            await Expect(Page.GetByText("Live response header check", new() { Exact = true })).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true }).ClickAsync();
            await Expect(Page.GetByText("Passed", new() { Exact = true }).Last).ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Link, new() { Name = "Monitoring and alerts" }).ClickAsync();
            await Page.GetByLabel("Test case to monitor").SelectOptionAsync("1");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Schedule" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Check now" })).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).ClickAsync();
            await WaitForMonitorHistoryAsync(database, 1);
            await Page.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
            await Expect(Page.GetByText("Monitoring history")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Passed", new() { Exact = true }).Last).ToBeVisibleAsync();

            await using var evidenceConnection = new SqlConnection($"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True");
            await evidenceConnection.OpenAsync();
            var persistedRunId = 0; var persistedRunPassed = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await using var runCommand = evidenceConnection.CreateCommand(); runCommand.CommandText = "SELECT TOP (1) Id, Passed FROM Runs WHERE TestCaseId = 1 ORDER BY CreatedAt DESC";
                await using var reader = await runCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync()) { persistedRunId = reader.GetInt32(0); persistedRunPassed = reader.GetBoolean(1); await reader.CloseAsync(); break; }
                await Task.Delay(250);
            }
            Assert.True(persistedRunId > 0);
            Assert.True(persistedRunPassed);
            await using var headerCommand = evidenceConnection.CreateCommand(); headerCommand.CommandText = "SELECT Name, Value FROM RunResponseHeaders WHERE TestRunId = @runId"; headerCommand.Parameters.AddWithValue("@runId", persistedRunId);
            var persistedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var headerReader = await headerCommand.ExecuteReaderAsync();
            while (await headerReader.ReadAsync()) persistedHeaders.Add(headerReader.GetString(0), headerReader.GetString(1));
            Assert.Equal("ready", persistedHeaders["X-Lab-Result"]);
            Assert.DoesNotContain("X-Auth-Token", persistedHeaders.Keys);
            Assert.DoesNotContain("Set-Cookie", persistedHeaders.Keys);
        }
        finally
        {
            foreach (var process in processes.AsEnumerable().Reverse())
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                ProcessLogs.TryRemove(process.Id, out _);
                process.Dispose();
            }
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection("Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            await master.OpenAsync(); await using var command = master.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) info.Environment[key] = value;
        var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var logs = new ConcurrentQueue<string>(); ProcessLogs[process.Id] = logs;
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) logs.Enqueue(eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) logs.Enqueue(eventArgs.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
    }

    private static async Task WaitForHttpAsync(string url, Process process)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try { using var response = await client.GetAsync(url); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            if (process.HasExited)
            {
                var log = ProcessLogs.TryGetValue(process.Id, out var lines) ? string.Join(Environment.NewLine, lines) : "";
                throw new InvalidOperationException($"A required local service exited before becoming ready. Exit code: {process.ExitCode}. {log}");
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Local service did not become ready at {url}.");
    }

    private static async Task WaitForMonitorHistoryAsync(string database, int monitorId)
    {
        await using var connection = new SqlConnection($"Server=.\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True");
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM MonitorHistory WHERE MonitorId = @monitorId"; command.Parameters.AddWithValue("@monitorId", monitorId);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("The live monitoring job did not persist its first history record.");
    }
}