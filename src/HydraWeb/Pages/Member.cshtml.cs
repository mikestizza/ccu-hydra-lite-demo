using HydraWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydraWeb.Pages;

public class MemberModel : PageModel
{
    private readonly HydraDb _db;
    private readonly ColbyClient _colby;
    private readonly AppInfo _info;

    public MemberModel(HydraDb db, ColbyClient colby, AppInfo info)
    {
        _db = db;
        _colby = colby;
        _info = info;
    }

    public int Id { get; private set; }
    public Member? Member { get; private set; }
    public ColbyResult? Colby { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Id = id;
        Member = _db.GetMember(id);
        if (Member is null) return Page();

        Colby = await _colby.GetAccountsAsync(id, ct);
        try { _db.LogLookup(id, _info.Pod); } catch { /* non-critical */ }
        return Page();
    }
}
