using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ApiSentinel.Tests;

public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private readonly string _database = $"ApiSentinel_Test_{Guid.NewGuid():N}";
    private string _connection = "";
    private readonly string _testJwt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    private readonly string _bootstrapKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    private string? _oldJwt;
    private string? _oldConnection;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public Task InitializeAsync()
    {
        _connection = $"Server=.\\SQLEXPRESS;Database={_database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        _oldJwt = Environment.GetEnvironmentVariable("Jwt__Key");
        _oldConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        Environment.SetEnvironmentVariable("Jwt__Key", _testJwt);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connection);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Outbound:TestingLaboratoryPort"] = "5081", ["Admin:BootstrapKey"] = _bootstrapKey }));
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        Environment.SetEnvironmentVariable("Jwt__Key", _oldJwt);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _oldConnection);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection("Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Query_parameters_are_encoded_appended_and_disabled_values_are_omitted()
    {
        var lab = await StartLaboratoryAsync();
        try
        {
            var client = _client!;
            var account = await Register(client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            var execution = await client.PostAsJsonAsync("/api/execute", new { url = "http://127.0.0.1:5081/items?existing=kept", method = "GET", queryParameters = new object[] { new { name = "page", value = "2" }, new { name = "size", value = "1" }, new { name = "disabled", value = "x", isEnabled = false } } });
            execution.EnsureSuccessStatusCode();
            var result = await execution.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("\"page\":2", result.GetProperty("body").GetString());
            Assert.Contains("\"size\":1", result.GetProperty("body").GetString());
            Assert.DoesNotContain("disabled", result.GetProperty("body").GetString());
        }
        finally { lab.Kill(true); await lab.WaitForExitAsync(); lab.Dispose(); }
    }

    [Fact]
    public async Task Request_execution_uses_the_controlled_laboratory_headers_and_timeout()
    {
        var lab = await StartLaboratoryAsync();
        try
        {
            var client = _client!;
            var account = await Register(client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            var savedApiResponse = await client.PostAsJsonAsync("/api/apis", new { name = "Lab header check", url = "http://127.0.0.1:5081/header-check", method = "GET", timeoutSeconds = 3 });
            savedApiResponse.EnsureSuccessStatusCode();
            var savedApi = await savedApiResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, savedApi.GetProperty("timeoutSeconds").GetInt32());
            var apiId = savedApi.GetProperty("id").GetInt32();
            var testResponse = await client.PostAsJsonAsync("/api/test-cases", new
            {
                apiId,
                name = "Response header is present",
                expectedStatus = 200,
                assertions = new[] { new { jsonPath = "header:X-Lab-Result", @operator = "Equals", expected = JsonSerializer.SerializeToElement("ready") } }
            });
            testResponse.EnsureSuccessStatusCode();
            var testCase = await testResponse.Content.ReadFromJsonAsync<JsonElement>();
            var execution = await client.PostAsJsonAsync("/api/execute", new { url = "", method = "GET", testCaseId = testCase.GetProperty("id").GetInt32() });
            execution.EnsureSuccessStatusCode();
            var result = await execution.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.GetProperty("passed").GetBoolean());
            Assert.True(result.GetProperty("assertions")[0].GetProperty("passed").GetBoolean());
            Assert.Equal("ready", result.GetProperty("headers").GetProperty("X-Lab-Result").GetString());
            var savedEvidence = (await client.GetFromJsonAsync<JsonElement>("/api/runs")).GetProperty("items").EnumerateArray().Single(run => run.GetProperty("id").GetInt32() == result.GetProperty("id").GetInt32());
            Assert.True(savedEvidence.GetProperty("assertions")[0].GetProperty("passed").GetBoolean());
            Assert.Contains(savedEvidence.GetProperty("headers").EnumerateArray(), header => header.GetProperty("name").GetString() == "X-Lab-Result");
            Assert.DoesNotContain(savedEvidence.GetProperty("headers").EnumerateArray(), header => header.GetProperty("name").GetString() == "Set-Cookie");
            Assert.DoesNotContain(savedEvidence.GetProperty("headers").EnumerateArray(), header => header.GetProperty("name").GetString() == "X-Auth-Token");

            var monitorResponse = await client.PostAsJsonAsync("/api/monitors", new { testCaseId = testCase.GetProperty("id").GetInt32(), frequencyMinutes = 1440, enabled = true });
            monitorResponse.EnsureSuccessStatusCode();
            var monitorId = (await monitorResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/monitors/{monitorId}/run-now", null)).StatusCode);
            var monitorHistory = await WaitForMonitorHistoryAsync(client, monitorId);
            Assert.True(monitorHistory.GetProperty("items")[0].GetProperty("passed").GetBoolean());
            var monitoredRun = (await client.GetFromJsonAsync<JsonElement>("/api/runs")).GetProperty("items").EnumerateArray().First(run => run.GetProperty("testCaseId").GetInt32() == testCase.GetProperty("id").GetInt32());
            Assert.True(monitoredRun.GetProperty("assertions")[0].GetProperty("passed").GetBoolean());
            Assert.DoesNotContain(monitoredRun.GetProperty("headers").EnumerateArray(), header => header.GetProperty("name").GetString() == "Set-Cookie");
            Assert.DoesNotContain(monitoredRun.GetProperty("headers").EnumerateArray(), header => header.GetProperty("name").GetString() == "X-Auth-Token");

            var failingApiResponse = await client.PostAsJsonAsync("/api/apis", new { name = "Lab failure", url = "http://127.0.0.1:5081/status/500", method = "GET", timeoutSeconds = 3 });
            failingApiResponse.EnsureSuccessStatusCode();
            var failingApiId = (await failingApiResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var failingTestResponse = await client.PostAsJsonAsync("/api/test-cases", new { apiId = failingApiId, name = "Expect success status", expectedStatus = 200 });
            failingTestResponse.EnsureSuccessStatusCode();
            var failingTestId = (await failingTestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var failingMonitorResponse = await client.PostAsJsonAsync("/api/monitors", new { testCaseId = failingTestId, frequencyMinutes = 1440, enabled = true });
            failingMonitorResponse.EnsureSuccessStatusCode();
            var failingMonitorId = (await failingMonitorResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/monitors/{failingMonitorId}/run-now", null)).StatusCode);
            var failedHistory = await WaitForMonitorHistoryAsync(client, failingMonitorId);
            Assert.False(failedHistory.GetProperty("items")[0].GetProperty("passed").GetBoolean());
            var alerts = await client.GetFromJsonAsync<JsonElement>("/api/alerts");
            var alertId = alerts.GetProperty("items")[0].GetProperty("id").GetInt32();
            (await client.PutAsJsonAsync($"/api/alerts/{alertId}/read", new { isRead = true })).EnsureSuccessStatusCode();
            Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/alerts")).GetProperty("unread").GetInt32());

            var serverErrorResponse = await client.PostAsJsonAsync("/api/execute", new { url = "http://127.0.0.1:5081/status/500", method = "GET", expectedStatus = 500, timeoutSeconds = 3 });
            serverErrorResponse.EnsureSuccessStatusCode();
            var serverError = await serverErrorResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(500, serverError.GetProperty("status").GetInt32());
            Assert.True(serverError.GetProperty("passed").GetBoolean());

            var timeout = await client.PostAsJsonAsync("/api/execute", new { url = "http://127.0.0.1:5081/delay/3000", method = "GET", timeoutSeconds = 1 });
            Assert.Equal(HttpStatusCode.GatewayTimeout, timeout.StatusCode);
            Assert.Contains("timed out", (await timeout.Content.ReadAsStringAsync()).ToLowerInvariant());
        }
        finally
        {
            if (!lab.HasExited) { lab.Kill(entireProcessTree: true); await lab.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task Auth_ownership_test_cases_monitoring_and_reports_work_end_to_end()
    {
        var client = _client!;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/apis")).StatusCode);

        var first = await Register(client);
        await using (var sql = new SqlConnection(_connection))
        {
            await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT TOP (1) TokenHash FROM RefreshTokens";
            var storedRefreshHash = (string)(await command.ExecuteScalarAsync())!;
            Assert.NotEqual(first.RefreshToken, storedRefreshHash);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"api_sentinel_refresh={first.RefreshToken}");
        var rotatedSession = await client.SendAsync(refreshRequest);
        rotatedSession.EnsureSuccessStatusCode();
        var rotated = await rotatedSession.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = rotatedSession.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=', 2)[1];
        Assert.NotEqual(first.RefreshToken, rotatedRefreshToken);
        await using (var sql = new SqlConnection(_connection))
        {
            await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM RefreshTokens WHERE TokenHash = @hash";
            command.Parameters.AddWithValue("@hash", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(first.RefreshToken))));
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replayRequest.Headers.Add("Cookie", $"api_sentinel_refresh={first.RefreshToken}");
        using var replayClient = _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await replayClient.SendAsync(replayRequest)).StatusCode);
        first = (rotated.GetProperty("token").GetString()!, rotatedRefreshToken, first.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/profile")).StatusCode);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/account/profile");
        Assert.Equal(first.Email, profile.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/account/password", new { currentPassword = "wrong", newPassword = "Another-secure-test-password!" })).StatusCode);
        var collectionResponse = await client.PostAsJsonAsync("/api/collections", new { name = "Integration collection" });
        collectionResponse.EnsureSuccessStatusCode();
        var collection = await collectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var collectionId = collection.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/collections/{collectionId}", new { name = "Renamed integration collection" })).StatusCode);

        var environmentResponse = await client.PostAsJsonAsync("/api/environments", new { name = "Staging", isActive = true });
        environmentResponse.EnsureSuccessStatusCode();
        var environmentId = (await environmentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await client.PutAsJsonAsync($"/api/environments/{environmentId}/variables", new { key = "API_BASE", value = "http://127.0.0.1:5080", isSecret = false })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/environments/{environmentId}/variables", new { key = "SERVICE_TOKEN", value = "environment-secret-for-test", isSecret = true })).EnsureSuccessStatusCode();
        var environmentList = await client.GetFromJsonAsync<JsonElement>("/api/environments");
        var secretVariable = environmentList.EnumerateArray().Single().GetProperty("variables").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "SERVICE_TOKEN");
        Assert.True(secretVariable.GetProperty("isSecret").GetBoolean());
        Assert.True(secretVariable.GetProperty("hasValue").GetBoolean());
        Assert.False(secretVariable.TryGetProperty("value", out var exposedSecret) && exposedSecret.ValueKind != JsonValueKind.Null);
        await using (var sql = new SqlConnection(_connection))
        {
            await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT EncryptedValue FROM EnvironmentVariables WHERE EnvironmentId = @id AND [Key] = 'SERVICE_TOKEN'"; command.Parameters.AddWithValue("@id", environmentId);
            var encrypted = (string)(await command.ExecuteScalarAsync())!; Assert.DoesNotContain("environment-secret-for-test", encrypted);
        }
        var templateApiResponse = await client.PostAsJsonAsync("/api/apis", new { name = "Environment URL", url = "{{API_BASE}}/api/health", method = "GET", collectionId, extractionJsonPath = "$.token", extractionVariableName = "AUTH_TOKEN" });
        templateApiResponse.EnsureSuccessStatusCode();
        var templateApi = await templateApiResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("{{API_BASE}}/api/health", templateApi.GetProperty("url").GetString());
        Assert.Equal("AUTH_TOKEN", templateApi.GetProperty("extractionVariableName").GetString());

        var imported = await client.PostAsJsonAsync("/api/apis/import-openapi", new
        {
            collectionId,
            specification = """{"openapi":"3.0.3","servers":[{"url":"https://example.com/v1"}],"components":{"securitySchemes":{"apiKeyAuth":{"type":"apiKey","in":"header","name":"X-API-Key"}}},"paths":{"/health":{"get":{"summary":"Health"},"post":{"operationId":"createHealth","requestBody":{"content":{"application/json":{"schema":{"type":"object","properties":{"name":{"type":"string"},"count":{"type":"integer"}}}}}}}},"/users/{userId}":{"get":{"security":[{"apiKeyAuth":[]}],"parameters":[{"name":"userId","in":"path","required":true,"schema":{"type":"string"}},{"name":"include","in":"query","schema":{"type":"boolean"}},{"name":"X-Region","in":"header","schema":{"type":"string"}}]}}}}"""
        });
        imported.EnsureSuccessStatusCode();
        var importedData = await imported.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, importedData.GetProperty("imported").GetInt32());
        Assert.Contains("https://example.com/v1/health", importedData.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("url").GetString()));
        Assert.Contains("https://example.com/v1/users/{{userId}}", importedData.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("url").GetString()));
        var importedPost = importedData.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("method").GetString() == "POST");
        Assert.Equal("{\"name\":\"\",\"count\":0}", importedPost.GetProperty("requestBody").GetString());
        var importedUser = importedData.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("url").GetString()!.Contains("/users/", StringComparison.Ordinal));
        Assert.Equal("{{X_Region}}", importedUser.GetProperty("headers").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.Equal("ApiKey", importedUser.GetProperty("authenticationType").GetString());
        Assert.Equal("X-API-Key", importedUser.GetProperty("authHeaderName").GetString());
        Assert.Equal("include", importedUser.GetProperty("queryParameters")[0].GetProperty("name").GetString());
        Assert.Equal("{{include}}", importedUser.GetProperty("queryParameters")[0].GetProperty("value").GetString());
        var yamlImport = await client.PostAsJsonAsync("/api/apis/import-openapi", new
        {
            collectionId,
            specification = "openapi: 3.0.3\ninfo:\n  title: Lab YAML API\n  version: 1.0.0\nservers:\n  - url: https://example.com/v1\npaths:\n  /yaml-check:\n    get:\n      summary: YAML health\n      security:\n        - labBearer: []\n      responses:\n        '200':\n          description: OK\ncomponents:\n  securitySchemes:\n    labBearer:\n      type: http\n      scheme: bearer\n"
        });
        yamlImport.EnsureSuccessStatusCode();
        var yamlImportData = await yamlImport.Content.ReadFromJsonAsync<JsonElement>();
        var yamlApiId = yamlImportData.GetProperty("items")[0].GetProperty("id").GetInt32();
        Assert.Equal("https://example.com/v1/yaml-check", yamlImportData.GetProperty("items")[0].GetProperty("url").GetString());
        Assert.Equal("Bearer", yamlImportData.GetProperty("items")[0].GetProperty("authenticationType").GetString());
        Assert.False(yamlImportData.GetProperty("items")[0].GetProperty("hasCredential").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/apis/import-openapi", new { specification = "openapi: [not valid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/apis/{yamlApiId}")).StatusCode);

        const string credential = "integration-test-bearer-token";
        const string savedHeaderSecret = "integration-header-secret";
        const string cookieSecret = "integration-cookie-session-secret";
        var apiResponse = await client.PostAsJsonAsync("/api/apis", new { name = "Public sample", url = "https://example.com", method = "POST", requestBody = "{\"check\":true}", contentType = "application/json", headers = new object[] { new { name = "X-Lab-Token", value = savedHeaderSecret, isSecret = true }, new { name = "Accept", value = "application/json", isSecret = false }, new { name = "Cookie", value = cookieSecret, isSecret = false }, new { name = "X-Auth-Token", value = "integration-auth-token", isSecret = false } }, collectionId, authenticationType = "Bearer", credential });
        apiResponse.EnsureSuccessStatusCode();
        var api = await apiResponse.Content.ReadFromJsonAsync<JsonElement>();
        var apiId = api.GetProperty("id").GetInt32();
        Assert.True(api.GetProperty("hasCredential").GetBoolean());
        Assert.False(api.TryGetProperty("credential", out _));
        Assert.Equal("{\"check\":true}", api.GetProperty("requestBody").GetString());
        var savedHeaders = api.GetProperty("headers").EnumerateArray().ToArray();
        Assert.True(savedHeaders.Single(h => h.GetProperty("name").GetString() == "X-Lab-Token").GetProperty("isSecret").GetBoolean());
        Assert.True(savedHeaders.Single(h => h.GetProperty("name").GetString() == "X-Lab-Token").GetProperty("value").ValueKind == JsonValueKind.Null);
        foreach (var secretHeaderName in new[] { "Cookie", "X-Auth-Token" })
        {
            var secretHeader = savedHeaders.Single(h => h.GetProperty("name").GetString() == secretHeaderName);
            Assert.True(secretHeader.GetProperty("isSecret").GetBoolean());
            Assert.Equal(JsonValueKind.Null, secretHeader.GetProperty("value").ValueKind);
        }
        await using (var sql = new SqlConnection(_connection))
        {
            await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT EncryptedCredential FROM Apis WHERE Id = @id"; command.Parameters.AddWithValue("@id", apiId);
            var stored = (string)(await command.ExecuteScalarAsync())!; Assert.DoesNotContain(credential, stored);
            await using var headerCommand = sql.CreateCommand(); headerCommand.CommandText = "SELECT EncryptedValue FROM ApiRequestHeaders WHERE ApiId = @id AND Name = 'X-Lab-Token'"; headerCommand.Parameters.AddWithValue("@id", apiId);
            var headerStored = (string)(await headerCommand.ExecuteScalarAsync())!; Assert.DoesNotContain(savedHeaderSecret, headerStored);
            await using var cookieCommand = sql.CreateCommand(); cookieCommand.CommandText = "SELECT Value, EncryptedValue FROM ApiRequestHeaders WHERE ApiId = @id AND Name = 'Cookie'"; cookieCommand.Parameters.AddWithValue("@id", apiId);
            await using var cookieReader = await cookieCommand.ExecuteReaderAsync(); Assert.True(await cookieReader.ReadAsync()); Assert.True(cookieReader.IsDBNull(0)); Assert.DoesNotContain(cookieSecret, cookieReader.GetString(1));
        }
        var preserveCredential = await client.PutAsJsonAsync($"/api/apis/{apiId}", new { name = "Public sample renamed", url = "https://example.com", method = "POST", requestBody = "{\"check\":true}", contentType = "application/json", collectionId, authenticationType = "Bearer" });
        preserveCredential.EnsureSuccessStatusCode();
        var updatedApi = await preserveCredential.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(updatedApi.GetProperty("hasCredential").GetBoolean());
        Assert.Contains(updatedApi.GetProperty("headers").EnumerateArray(), h => h.GetProperty("name").GetString() == "X-Lab-Token");

        var testResponse = await client.PostAsJsonAsync("/api/test-cases", new { apiId, name = "Status and body", expectedStatus = 200, schemaJson = """{"type":"object","required":["id"],"properties":{"id":{"type":"integer"}}}""", assertions = Array.Empty<object>() });
        testResponse.EnsureSuccessStatusCode();
        var test = await testResponse.Content.ReadFromJsonAsync<JsonElement>();
        var testId = test.GetProperty("id").GetInt32();
        Assert.True(test.GetProperty("schemaJson").GetString()!.Contains("integer", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/test-cases", new { apiId, name = "Invalid schema", schemaJson = "{\"type\":\"executable\"}" })).StatusCode);

        var monitorResponse = await client.PostAsJsonAsync("/api/monitors", new { testCaseId = testId, frequencyMinutes = 1440, enabled = true });
        monitorResponse.EnsureSuccessStatusCode();
        var monitor = await monitorResponse.Content.ReadFromJsonAsync<JsonElement>();
        var monitorId = monitor.GetProperty("id").GetInt32();
        var updateMonitor = await client.PutAsJsonAsync($"/api/monitors/{monitorId}", new { testCaseId = testId, frequencyMinutes = 60, enabled = false });
        updateMonitor.EnsureSuccessStatusCode();

        var ownTests = await client.GetFromJsonAsync<JsonElement>("/api/test-cases");
        Assert.Equal(1, ownTests.GetProperty("total").GetInt32());
        var report = await client.GetFromJsonAsync<JsonElement>("/api/reports/dashboard");
        Assert.Equal(5, report.GetProperty("apis").GetInt32());
        Assert.Equal(1, report.GetProperty("testCases").GetInt32());

        var blockedTarget = await client.PostAsJsonAsync("/api/execute", new { url = "http://127.0.0.1:5080/api/health", method = "GET" });
        Assert.Equal(HttpStatusCode.BadRequest, blockedTarget.StatusCode);
        var failedExecution = await blockedTarget.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(failedExecution.GetProperty("failed").GetBoolean());
        Assert.True(failedExecution.GetProperty("runId").GetInt32() > 0);
        var templatedExecution = await client.PostAsJsonAsync("/api/execute", new { url = "{{API_BASE}}/api/health", method = "GET" });
        Assert.Equal(HttpStatusCode.BadRequest, templatedExecution.StatusCode);
        Assert.Contains("restricted network", (await templatedExecution.Content.ReadAsStringAsync()).ToLowerInvariant());
        var userInfoTarget = await client.PostAsJsonAsync("/api/execute", new { url = "http://user:password@example.com/", method = "GET" });
        Assert.Equal(HttpStatusCode.BadRequest, userInfoTarget.StatusCode);
        Assert.Contains("embedded credentials", (await userInfoTarget.Content.ReadAsStringAsync()).ToLowerInvariant());
        var history = await client.GetFromJsonAsync<JsonElement>("/api/runs");
        Assert.Equal(3, history.GetProperty("total").GetInt32());
        Assert.All(history.GetProperty("items").EnumerateArray(), item => { Assert.False(item.GetProperty("passed").GetBoolean()); Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("error").GetString())); });
        Assert.Equal("{{API_BASE}}/api/health", history.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("url").GetString()!.StartsWith("{{API_BASE}}", StringComparison.Ordinal)).GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/apis/{apiId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/monitors")).EnumerateArray());
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/test-cases")).GetProperty("total").GetInt32());

        var second = await Register(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        (await client.PutAsJsonAsync("/api/account/password", new { currentPassword = "A-long-secure-test-password!", newPassword = "Another-secure-test-password!" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/profile")).StatusCode);
        var renewed = await client.PostAsJsonAsync("/api/auth/login", new { email = first.Email, password = "Another-secure-test-password!" });
        renewed.EnsureSuccessStatusCode();
        var renewedToken = (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewedToken);
        var changedEmail = $"updated-{Guid.NewGuid():N}@example.test";
        (await client.PutAsJsonAsync("/api/account/profile", new { email = changedEmail, currentPassword = "Another-secure-test-password!" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/profile")).StatusCode);
        var afterEmailChange = await client.PostAsJsonAsync("/api/auth/login", new { email = changedEmail, password = "Another-secure-test-password!" });
        afterEmailChange.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await afterEmailChange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        (await client.PostAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/profile")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Token);
        var otherTests = await client.GetFromJsonAsync<JsonElement>("/api/test-cases");
        Assert.Equal(0, otherTests.GetProperty("total").GetInt32());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/environments")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/collections/{collectionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/monitors/{monitorId}")).StatusCode);

        var bootstrapAccount = await Register(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bootstrapAccount.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        (await client.PostAsJsonAsync("/api/admin/bootstrap", new { key = _bootstrapKey })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/profile")).StatusCode);
        var adminLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = bootstrapAccount.Email, password = "A-long-secure-test-password!" });
        adminLogin.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await adminLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        var users = await client.GetFromJsonAsync<JsonElement>("/api/admin/users");
        Assert.Equal(3, users.GetProperty("total").GetInt32());
        var secondUserId = users.GetProperty("items").EnumerateArray().Single(user => user.GetProperty("email").GetString() == second.Email).GetProperty("id").GetInt32();
        (await client.PutAsJsonAsync($"/api/admin/users/{secondUserId}/role", new { role = "Admin" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/admin/users/{users.GetProperty("items").EnumerateArray().Single(user => user.GetProperty("email").GetString() == bootstrapAccount.Email).GetProperty("id").GetInt32()}/role", new { role = "User" })).StatusCode);
        var promotedLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = second.Email, password = "A-long-secure-test-password!" });
        promotedLogin.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await promotedLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        var promotedProfile = await client.GetFromJsonAsync<JsonElement>("/api/account/profile");
        Assert.Contains(promotedProfile.GetProperty("roles").EnumerateArray(), role => role.GetString() == "Admin");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    private static async Task<(string Token, string RefreshToken, string Email)> Register(HttpClient client)
    {
        var email = $"test-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "A-long-secure-test-password!" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=', 2)[1];
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        return (json.GetProperty("token").GetString()!, refreshToken, email);
    }

    private static async Task<Process> StartLaboratoryAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var labProject = Path.Combine(root, "backend", "ApiTestingLaboratory");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "run", "--no-build", "--project", labProject, "--urls", "http://127.0.0.1:5081" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the controlled API laboratory.");
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { using var response = await probe.GetAsync("http://127.0.0.1:5081/"); if (response.IsSuccessStatusCode) return process; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            if (process.HasExited) throw new InvalidOperationException("The controlled API laboratory exited during startup.");
            await Task.Delay(250);
        }
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw new TimeoutException("The controlled API laboratory did not start.");
    }

    private static async Task<JsonElement> WaitForMonitorHistoryAsync(HttpClient client, int monitorId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var history = await client.GetFromJsonAsync<JsonElement>($"/api/monitors/{monitorId}/history");
            if (history.GetProperty("total").GetInt32() > 0) return history;
            await Task.Delay(250);
        }
        throw new TimeoutException("The queued monitoring execution did not persist its history.");
    }
}