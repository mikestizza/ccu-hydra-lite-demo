namespace HydraWeb.Services;

/// <summary>
/// Stands in for Hangfire running in-process inside the web app, which is how
/// Hydra runs today on WEB01/WEB02. Every replica runs this loop, exactly like
/// every IIS worker runs Hangfire. Job results land in SQL so all replicas see
/// the same history.
/// </summary>
public sealed class JobRunner : BackgroundService
{
    private static readonly (string Name, int MinMs, int MaxMs)[] Jobs =
    {
        ("DNA core balance refresh", 400, 1800),
        ("Card status sync (Fiserv)", 250, 900),
        ("UKG timecard extraction", 900, 2600),
        ("Address standardization batch", 150, 600),
        ("Candescent session cleanup", 100, 400),
    };

    private readonly HydraDb _db;
    private readonly AppInfo _info;
    private readonly ILogger<JobRunner> _log;
    private readonly TimeSpan _interval;

    public JobRunner(HydraDb db, AppInfo info, IConfiguration cfg, ILogger<JobRunner> log)
    {
        _db = db;
        _info = info;
        _log = log;
        var seconds = int.TryParse(cfg["Hydra:JobIntervalSeconds"], out var s) ? s : 45;
        _interval = TimeSpan.FromSeconds(Math.Max(10, seconds));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Let the database initializer finish first.
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        var rng = new Random();

        while (!ct.IsCancellationRequested)
        {
            var job = Jobs[rng.Next(Jobs.Length)];
            var duration = rng.Next(job.MinMs, job.MaxMs);
            var status = rng.Next(0, 20) == 0 ? "Retried" : "Succeeded";
            try
            {
                await Task.Delay(Math.Min(duration, 1500), ct); // simulate work, capped so the loop stays snappy
                _db.RecordJob(job.Name, duration, status, _info.Pod);
                _log.LogInformation("job={Job} status={Status} durationMs={Duration}", job.Name, status, duration);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("job={Job} failed: {Message}", job.Name, ex.Message);
            }

            try { await Task.Delay(_interval, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
