using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using System.Diagnostics;
using System.Net;

namespace ApiSentinel.Tests;

public sealed class WorkspaceBrowserTests : PageTest
{
    [Fact]
    public async Task User_can_sign_in_and_execute_a_request_from_the_workspace()
    {
        var (baseUrl, vite) = await EnsureFrontendAsync();
        var apiSaved = false;
        var testSaved = false;
        var environmentSaved = false;
        var collectionSaved = false;
        var monitorSaved = false;
        var alertRead = false;
        var apiSearchRequested = false;
        var queryParameterSent = false;
        var managedRole = "User";
        var expireNextReport = false;
        var refreshWasRequested = false;
        var allowRefresh = false;
        var refreshRequestCount = 0;
        try
        {
        await Page.RouteAsync("http://localhost:5080/api/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/apis" && new Uri(route.Request.Url).Query.Contains("search=health", StringComparison.OrdinalIgnoreCase)) apiSearchRequested = true;
            var (status, body) = path switch
            {
                "/api/auth/login" => (200, "{\"token\":\"browser-test-token\",\"refreshToken\":\"browser-test-refresh-token\",\"user\":\"browser@example.test\"}"),
                "/api/auth/refresh" => RefreshSession(),
                "/api/account/profile" => (200, "{\"id\":1,\"email\":\"browser@example.test\",\"roles\":[\"Admin\"]}"),
                "/api/account/password" => (204, ""),
                "/api/account/logout" => (204, ""),
                "/api/apis" when route.Request.Method == "POST" => SaveApi(),
                "/api/apis/import-openapi" => (200, "{\"imported\":1,\"unsupported\":[],\"items\":[{\"id\":2,\"name\":\"Imported health\",\"url\":\"https://example.com/health\",\"method\":\"GET\"}]}"),
                "/api/apis" => (200, apiSaved ? "{\"total\":1,\"items\":[{\"id\":1,\"name\":\"Health API\",\"url\":\"https://example.com/health\",\"method\":\"GET\",\"collectionId\":null,\"authenticationType\":\"None\",\"hasCredential\":false,\"headers\":[]}] }" : "{\"total\":0,\"items\":[]}"),
                "/api/test-cases" when route.Request.Method == "POST" => SaveTest(),
                "/api/test-cases" => (200, testSaved ? "{\"total\":1,\"items\":[{\"id\":1,\"apiId\":1,\"name\":\"Health returns 200\",\"expectedStatus\":200,\"maxDurationMs\":null,\"assertions\":[]}] }" : "{\"total\":0,\"items\":[]}"),
                "/api/runs" => (200, "{\"total\":1,\"items\":[{\"id\":9,\"testCaseId\":null,\"url\":\"https://example.com/previous\",\"method\":\"GET\",\"status\":503,\"durationMs\":80,\"body\":\"{\\\"error\\\":\\\"unavailable\\\"}\",\"error\":\"\",\"passed\":false,\"createdAt\":\"2026-01-01T00:00:00Z\"}] }"),
                "/api/collections" when route.Request.Method == "POST" => SaveCollection(),
                "/api/collections" => (200, collectionSaved ? "[{\"id\":1,\"name\":\"Smoke suite\",\"requestCount\":0}]" : "[]"),
                "/api/environments" when route.Request.Method == "POST" => SaveEnvironment(),
                "/api/environments" => (200, environmentSaved ? "[{\"id\":1,\"name\":\"Staging\",\"isActive\":true,\"createdAt\":\"2026-01-01T00:00:00Z\",\"variables\":[]}]" : "[]"),
                "/api/monitors" when route.Request.Method == "POST" => SaveMonitor(),
                "/api/monitors" => (200, monitorSaved ? "[{\"id\":1,\"testCaseId\":1,\"testName\":\"Health returns 200\",\"apiUrl\":\"https://example.com/health\",\"frequencyMinutes\":15,\"enabled\":true,\"lastCheck\":null}]" : "[]"),
                "/api/monitors/1/run-now" => (202, "{\"jobId\":\"browser-job\"}"),
                "/api/monitors/1/history" => (200, "{\"total\":1,\"items\":[{\"monitorName\":\"Health returns 200\",\"url\":\"https://example.com/health\",\"status\":200,\"durationMs\":11,\"passed\":true,\"error\":\"\",\"createdAt\":\"2026-01-01T00:00:00Z\"}]}"),
                "/api/alerts/1/read" when route.Request.Method == "PUT" => SaveAlertRead(),
                "/api/admin/users/2/role" when route.Request.Method == "PUT" => SetManagedRole(route.Request.PostData),
                "/api/admin/users" => (200, AdminUsers()),
                "/api/alerts" => (200, alertRead ? "{\"unread\":0,\"total\":1,\"items\":[{\"id\":1,\"title\":\"API recovered\",\"message\":\"Health check recovered.\",\"isRead\":true,\"createdAt\":\"2026-01-01T00:00:00Z\"}]}" : "{\"unread\":1,\"total\":1,\"items\":[{\"id\":1,\"title\":\"Monitoring check failed\",\"message\":\"Health check failed.\",\"isRead\":false,\"createdAt\":\"2026-01-01T00:00:00Z\"}]}"),
                "/api/reports/dashboard" => ReportResponse(),
                "/api/ai/generate" => (200, "{\"aiAssisted\":true,\"expectedStatus\":200,\"rationale\":\"Check the success response.\",\"assertions\":[{\"jsonPath\":\"$.ok\",\"operator\":\"Equals\",\"expected\":true}]}"),
                "/api/ai/analyze/9" => (200, "{\"summary\":\"The target returned 503.\",\"likelyCause\":\"The service was unavailable.\",\"hypotheses\":[],\"debuggingSteps\":[],\"recommendedTests\":[]}"),
                "/api/ai/recommendations/1" => (200, "{\"recommendations\":[{\"name\":\"Reject invalid input\",\"expectedStatus\":400,\"rationale\":\"Verify validation behavior.\"}]}"),
                "/api/ai/summary" => (200, "{\"aiAssisted\":true,\"runsAnalyzed\":1,\"summary\":\"One recent failure.\",\"commonErrors\":[],\"regressions\":[],\"performanceObservations\":[],\"suggestedActions\":[]}"),
                "/api/execute" => ExecuteRequest(route.Request.PostData),
                _ => (200, "{}")
            };
            await route.FulfillAsync(new RouteFulfillOptions { Status = status, ContentType = "application/json", Body = body });
        });

        await Page.GotoAsync(baseUrl);
        await Page.GetByLabel("Email").FillAsync("browser@example.test");
        await Page.GetByLabel("Password").FillAsync("browser-test-password");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "API overview" })).ToBeVisibleAsync();
        Assert.Equal(1, refreshRequestCount);
        Assert.Null(await Page.EvaluateAsync<string?>("localStorage.getItem('token')"));
        Assert.Null(await Page.EvaluateAsync<string?>("localStorage.getItem('refreshToken')"));

        await Page.GetByRole(AriaRole.Link, new() { Name = "API workspace" }).ClickAsync();
        await Page.GetByLabel("Request URL").FillAsync("https://example.com/health");
        await Page.GetByText("Query parameters", new() { Exact = false }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add query parameter" }).ClickAsync();
        await Page.GetByLabel("Query parameter 1 name").FillAsync("page");
        await Page.GetByLabel("Query parameter 1 value").FillAsync("2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send request" }).ClickAsync();
        await Expect(Page.Locator(".responsehead").Filter(new() { HasText = "PASS" })).ToBeVisibleAsync();
        Assert.True(queryParameterSent, "The request editor should send configured query parameters.");

        await Page.GetByLabel("API name").FillAsync("Health API");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save request" }).ClickAsync();
        await Expect(Page.GetByText("Health API", new() { Exact = true }).First).ToBeVisibleAsync();
        await Page.GetByLabel("Search APIs").FillAsync("health");
        await Expect(Page.GetByText("Health API", new() { Exact = true }).First).ToBeVisibleAsync();
        Assert.True(apiSearchRequested, "The API workspace should send search terms to the backend.");
        await Page.GetByLabel("OpenAPI JSON").FillAsync("{\"openapi\":\"3.0.3\",\"paths\":{}}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Import endpoints" }).ClickAsync();
        await Expect(Page.GetByText("Imported 1 endpoints.")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Tests and collections" }).ClickAsync();
        await Page.GetByLabel("API for test").SelectOptionAsync("1");
        await Page.GetByPlaceholder("Test name").FillAsync("Health returns 200");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save test case" }).ClickAsync();
        await Expect(Page.GetByText("Health returns 200", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "API workspace" }).ClickAsync();
        await Expect(Page.Locator(".responsehead").Filter(new() { HasText = "PASS" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Environments" }).ClickAsync();
        await Page.GetByLabel("Environment name").FillAsync("Staging");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create environment" }).ClickAsync();
        await Expect(Page.GetByText("Staging", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Tests and collections" }).ClickAsync();
        await Page.GetByLabel("Collection name").FillAsync("Smoke suite");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Smoke suite", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Monitoring and alerts" }).ClickAsync();
        await Page.GetByLabel("Test case to monitor").SelectOptionAsync("1");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Schedule" }).ClickAsync();
        await Expect(Page.Locator("b").Filter(new() { HasText = "Health returns 200" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        await Expect(Page.GetByText("11 ms", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Mark read" }).ClickAsync();
        await Expect(Page.GetByText("0 UNREAD")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Reports" }).ClickAsync();
        expireNextReport = true;
        await Page.GetByLabel("Report date range").SelectOptionAsync("7");
        await Expect(Page.GetByText("Reports and analytics")).ToBeVisibleAsync();
        for (var attempt = 0; attempt < 40 && refreshRequestCount < 2; attempt++) await Task.Delay(50);
        Assert.True(refreshWasRequested, "An expired access token should be renewed and the failed request retried.");
        Assert.Equal(2, refreshRequestCount);

        await Page.GetByRole(AriaRole.Link, new() { Name = "User administration" }).ClickAsync();
        await Expect(Page.GetByText("user@example.test", new() { Exact = true })).ToBeVisibleAsync();
        await Page.GetByLabel("Role for user@example.test").SelectOptionAsync("Admin");
        await Expect(Page.GetByLabel("Role for user@example.test")).ToHaveValueAsync("Admin");

        await Page.GetByRole(AriaRole.Link, new() { Name = "AI assistant" }).ClickAsync();
        await Page.GetByLabel("Test idea").FillAsync("Check the health success response");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate test suggestion" }).ClickAsync();
        await Expect(Page.GetByText("Check the success response.")).ToBeVisibleAsync();
        await Page.GetByLabel("Failed execution").SelectOptionAsync("9");
        await Expect(Page.GetByText("The target returned 503.")).ToBeVisibleAsync();
        await Page.GetByLabel("Saved API").SelectOptionAsync("1");
        await Expect(Page.GetByText("Reject invalid input")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Summarize recent executions" }).ClickAsync();
        await Expect(Page.GetByText("One recent failure.")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Profile and security" }).ClickAsync();
        await Page.GetByLabel("Current password").FillAsync("browser-test-password");
        await Page.GetByLabel("New password").FillAsync("browser-new-password-123");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Change password" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeVisibleAsync();
        }
        finally
        {
            if (vite is { HasExited: false }) { vite.Kill(entireProcessTree: true); await vite.WaitForExitAsync(); }
        }

        (int, string) SaveApi() { apiSaved = true; return (201, "{\"id\":1,\"name\":\"Health API\",\"url\":\"https://example.com/health\",\"method\":\"GET\",\"collectionId\":null,\"authenticationType\":\"None\",\"hasCredential\":false,\"headers\":[]}"); }
        (int, string) ExecuteRequest(string? postData) { using var payload = System.Text.Json.JsonDocument.Parse(postData ?? "{}"); queryParameterSent = payload.RootElement.TryGetProperty("queryParameters", out var parameters) && parameters.GetArrayLength() > 0 && parameters[0].GetProperty("name").GetString() == "page"; return (200, "{\"id\":1,\"status\":200,\"durationMs\":11,\"body\":\"{\\\"ok\\\":true}\",\"passed\":true,\"headers\":{\"content-type\":\"application/json\"}}"); }
        (int, string) SaveTest() { testSaved = true; return (201, "{\"id\":1,\"apiId\":1,\"name\":\"Health returns 200\",\"expectedStatus\":200,\"maxDurationMs\":null,\"assertions\":[]}"); }
        (int, string) SaveCollection() { collectionSaved = true; return (201, "{\"id\":1,\"name\":\"Smoke suite\",\"requestCount\":0}"); }
        (int, string) SaveEnvironment() { environmentSaved = true; return (201, "{\"id\":1,\"name\":\"Staging\",\"isActive\":true,\"createdAt\":\"2026-01-01T00:00:00Z\",\"variables\":[]}"); }
        (int, string) SaveMonitor() { monitorSaved = true; return (201, "{\"id\":1,\"testCaseId\":1,\"frequencyMinutes\":15,\"enabled\":true}"); }
        (int, string) SaveAlertRead() { alertRead = true; return (204, ""); }
        (int, string) RefreshSession() { refreshWasRequested = true; refreshRequestCount++; if (allowRefresh) { allowRefresh = false; return (200, "{\"token\":\"browser-refreshed-token\"}"); } return (401, "{}"); }
        (int, string) ReportResponse() { if (expireNextReport) { expireNextReport = false; allowRefresh = true; return (401, "{}"); } return (200, "{\"days\":14,\"apis\":0,\"testCases\":0,\"executions\":0,\"passed\":0,\"failed\":0,\"passRate\":0,\"averageResponseMs\":0,\"monitorAvailability\":0,\"daily\":[],\"slowest\":[],\"recentFailures\":[]}"); }
        (int, string) SetManagedRole(string? postData) { managedRole = System.Text.Json.JsonDocument.Parse(postData ?? "{}").RootElement.GetProperty("role").GetString() ?? "User"; return (200, "{}"); }
        string AdminUsers() => $"{{\"total\":1,\"items\":[{{\"id\":2,\"email\":\"user@example.test\",\"roles\":[\"{managedRole}\"]}}]}}";
    }

    private static async Task<(string Url, Process? Process)> EnsureFrontendAsync()
    {
        var url = Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://127.0.0.1:5173";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        try { using var response = await client.GetAsync(url); if (response.StatusCode == HttpStatusCode.OK) return (url, null); }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        var frontend = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "frontend"));
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "npm.cmd" : "npm")
        {
            Arguments = "run dev -- --host 127.0.0.1",
            WorkingDirectory = frontend,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Vite development server for browser tests.");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { using var response = await client.GetAsync(url); if (response.StatusCode == HttpStatusCode.OK) return (url, process); }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw new TimeoutException("Vite did not start for the Playwright browser test.");
    }
}