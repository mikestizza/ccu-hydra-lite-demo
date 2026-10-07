using HydraWeb.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydraWeb.Pages;

public class JobsModel : PageModel
{
    private readonly HydraDb _db;
    public JobsModel(HydraDb db) => _db = db;

    public List<JobRun> Runs { get; private set; } = new();
    public string? Error { get; private set; }

    public void OnGet()
    {
        try { Runs = _db.RecentJobs(30); }
        catch (Exception ex) { Error = "Job history unavailable: " + ex.Message; }
    }
}
