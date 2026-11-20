using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Api.Services;

public sealed class MonitoringJob(SentinelDb db, HttpRequestExecutor executor, CredentialProtector protector, EnvironmentResolver environmentResolver, ILogger<MonitoringJob> logger)
{
    private static List<ApiQueryParameterInput> SavedQueryParameters(ApiDefinition api, CredentialProtector protector) => api.QueryParameters.OrderBy(p => p.SortOrder)
        .Select(p => new ApiQueryParameterInput(p.Name, p.IsSecret ? protector.Unprotect(p.EncryptedValue!) : p.Value ?? "", p.IsSecret, p.IsEnabled)).ToList();

    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task ExecuteAsync(int monitorId, CancellationToken cancellationToken)
    {
        var monitor = await db.Monitors.Include(m => m.TestCase).ThenInclude(t => t.Api).ThenInclude(a => a.Headers).Include(m => m.TestCase).ThenInclude(t => t.Api).ThenInclude(a => a.QueryParameters).Include(m => m.TestCase).ThenInclude(t => t.Assertions)
            .SingleOrDefaultAsync(m => m.Id == monitorId && m.Enabled, cancellationToken);
        if (monitor is null) return;

        var test = monitor.TestCase; var url = test.Api.Url; var status = 0; long duration = 0; var error = ""; var passed = false; HttpExecutionResult? response = null;
        try
        {
            var credential = test.Api.EncryptedCredential is null ? null : protector.Unprotect(test.Api.EncryptedCredential);
            var variables = await environmentResolver.GetActiveAsync(monitor.UserId, cancellationToken);
            var headers = test.Api.Headers.ToDictionary(h => h.Name, h => h.IsSecret ? protector.Unprotect(h.EncryptedValue!) : h.Value ?? "", StringComparer.OrdinalIgnoreCase);
            response = await executor.ExecuteAsync(url, test.Api.Method, test.Api.RequestBody, test.Api.ContentType, headers, test.Api.AuthenticationType, credential, test.Api.AuthHeaderName, variables, cancellationToken, test.Api.TimeoutSeconds, SavedQueryParameters(test.Api, protector));
            status = response.Status; duration = response.DurationMs;
            var schemaError = test.SchemaJson is null ? null : JsonResponseSchemaValidator.Validate(response.Body, test.SchemaJson);
            passed = (test.ExpectedStatus is null ? status is >= 200 and < 300 : status == test.ExpectedStatus) && (test.MaxDurationMs is null || duration <= test.MaxDurationMs) && schemaError is null && test.Assertions.All(a => AssertionEvaluator.Evaluate(response.Body, a, response.Headers).Passed);
            if (!passed)
            {
                if (test.ExpectedStatus is not null && status != test.ExpectedStatus) error = $"Expected status {test.ExpectedStatus}; received {status}.";
                else if (test.ExpectedStatus is null && status is not (>= 200 and < 300)) error = $"Expected a 2xx status; received {status}.";
                else if (test.MaxDurationMs is not null && duration > test.MaxDurationMs) error = $"Response took {duration} ms; maximum is {test.MaxDurationMs} ms.";
                else error = schemaError ?? "One or more saved assertions failed.";
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            error = e is TaskCanceledException ? "Request timed out." : "Target request failed.";
            logger.LogWarning("Monitoring request {MonitorId} failed: {FailureType}", monitorId, e.GetType().Name);
        }

        var assertionResults = response is null ? [] : test.Assertions.Select(a => AssertionEvaluator.Evaluate(response.Body, a, response.Headers)).ToArray();
        var run = new TestRun { UserId = monitor.UserId, TestCaseId = test.Id, Url = url, Method = test.Api.Method, Status = status, DurationMs = duration, Body = "", Error = error, Passed = passed, CreatedAt = DateTime.UtcNow,
            Assertions = assertionResults.Select(a => new TestRunAssertion { JsonPath = a.JsonPath, Operator = a.Operator, Actual = a.Actual is { Length: > 8192 } ? a.Actual[..8192] : a.Actual, Expected = a.Expected is { Length: > 8192 } ? a.Expected[..8192] : a.Expected, Passed = a.Passed, Error = a.Error }).ToList(),
            ResponseHeaders = response is null ? [] : response.Headers.Where(h => !SensitiveValueClassifier.IsSensitiveName(h.Key)).Select(h => new TestRunResponseHeader { Name = h.Key, Value = h.Value.Length > 8192 ? h.Value[..8192] : h.Value }).ToList() };
        var history = new MonitorHistory { MonitorId = monitor.Id, MonitorName = test.Name, Url = url, Status = status, DurationMs = duration, Passed = passed, Error = error, CreatedAt = DateTime.UtcNow };
        var prior = await db.MonitorHistory.Where(h => h.MonitorId == monitorId).OrderByDescending(h => h.CreatedAt).Select(h => (bool?)h.Passed).FirstOrDefaultAsync(cancellationToken);
        db.Runs.Add(run); db.MonitorHistory.Add(history);
        if (prior is null && !passed || prior == true && !passed)
            db.Alerts.Add(new Alert { UserId = monitor.UserId, Title = "Monitoring check failed", Message = $"{test.Name}: {(error.Length == 0 ? "saved assertions failed" : error)}", CreatedAt = DateTime.UtcNow });
        else if (prior == false && passed)
            db.Alerts.Add(new Alert { UserId = monitor.UserId, Title = "API recovered", Message = $"{test.Name} is passing again.", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }
}