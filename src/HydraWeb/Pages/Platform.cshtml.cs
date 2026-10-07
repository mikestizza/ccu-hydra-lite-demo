using HydraWeb.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace HydraWeb.Pages;

public class PlatformModel : PageModel
{
    private readonly HydraDb _db;
    private readonly ColbyClient _colby;
    private readonly IConfiguration _cfg;

    public PlatformModel(HydraDb db, ColbyClient colby, IConfiguration cfg)
    {
        _db = db;
        _colby = colby;
        _cfg = cfg;
    }

    public string User { get; private set; } = "";
    public string SqlServer { get; private set; } = "";
    public string SqlTarget { get; private set; } = "";
    public string ColbyUrl { get; private set; } = "";

    public void OnGet()
    {
        User = Environment.UserName;
        SqlServer = _db.ServerInfo();
        ColbyUrl = _colby.BaseUrl;

        try
        {
            var b = new SqlConnectionStringBuilder(HydraDb.ResolveConnectionString(_cfg));
            SqlTarget = $"{b.DataSource} / {b.InitialCatalog} (user {b.UserID})";
        }
        catch { SqlTarget = "(unparseable)"; }
    }
}
