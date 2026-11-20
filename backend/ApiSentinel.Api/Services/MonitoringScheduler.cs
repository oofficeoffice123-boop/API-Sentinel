using Hangfire;

namespace ApiSentinel.Api.Services;

public sealed class MonitoringScheduler(IRecurringJobManager jobs)
{
    public string Enqueue(int monitorId) => BackgroundJob.Enqueue<MonitoringJob>(job => job.ExecuteAsync(monitorId, CancellationToken.None));

    public void Apply(int monitorId, int frequencyMinutes, bool enabled)
    {
        var jobId = $"monitor:{monitorId}";
        if (!enabled) { jobs.RemoveIfExists(jobId); return; }
        var cron = frequencyMinutes switch { 5 or 15 or 30 => $"*/{frequencyMinutes} * * * *", 60 => "0 * * * *", 1440 => "0 0 * * *", _ => throw new ArgumentOutOfRangeException(nameof(frequencyMinutes)) };
        jobs.AddOrUpdate<MonitoringJob>(jobId, job => job.ExecuteAsync(monitorId, CancellationToken.None), cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    public void Remove(int monitorId) => jobs.RemoveIfExists($"monitor:{monitorId}");
}