using HydraWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydraWeb.Pages;

public class MembersModel : PageModel
{
    private readonly HydraDb _db;

    public MembersModel(HydraDb db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<Member> Results { get; private set; } = new();
    public string? Error { get; private set; }

    public void OnGet()
    {
        try
        {
            Results = _db.SearchMembers(Q, 60);
        }
        catch (Exception ex)
        {
            Error = "Member database unavailable: " + ex.Message;
        }
    }
}
