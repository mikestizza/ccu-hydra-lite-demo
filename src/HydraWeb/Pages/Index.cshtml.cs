using HydraWeb.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydraWeb.Pages;

public class IndexModel : PageModel
{
    private readonly HydraDb _db;
    private readonly ColbyClient _colby;
    private readonly ILogger<IndexModel> _log;

    public IndexModel(HydraDb db, ColbyClient colby, ILogger<IndexModel> log)
    {
        _db = db;
        _colby = colby;
        _log = log;
    }

    public int Members { get; private set; }
    public int LookupsToday { get; private set; }
    public int JobsToday { get; private set; }
    public bool DbOk { get; private set; }
    public bool ColbyOk { get; private set; }
    public List<LookupEntry> Recent { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        DbOk = await _db.PingAsync(ct);
        ColbyOk = await _colby.PingAsync(ct);

        if (DbOk)
        {
            try
            {
                (Members, LookupsToday, JobsToday) = _db.Stats();
                Recent = _db.RecentLookups(8);
            }
            catch (Exception ex)
            {
                // Schema may still be initializing on first boot.
                _log.LogWarning("Dashboard query failed: {Message}", ex.Message);
                DbOk = false;
            }
        }
    }
}
