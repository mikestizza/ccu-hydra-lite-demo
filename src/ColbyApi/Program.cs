// Colby API (mock)
// Stands in for one of CCU's in-house ancillary APIs. In the real estate this
// is a service Hydra calls many times per member session. Here it returns
// deterministic mock account data so the web tier has something to talk to
// over the cluster network instead of hairpinning through a load balancer.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

var app = builder.Build();

var podName = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
var logger = app.Logger;

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "colby-api", pod = podName, version }));

app.MapGet("/", () => Results.Ok(new
{
    service = "colby-api",
    description = "Mock in-house ancillary API for the Hydra-lite demo",
    pod = podName,
    version,
    endpoints = new[] { "/healthz", "/api/members/{memberId}/accounts", "/api/members/{memberId}/cards" }
}));

app.MapGet("/api/members/{memberId:int}/accounts", (int memberId, HttpContext ctx) =>
{
    var caller = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    logger.LogInformation("accounts lookup member={MemberId} caller={Caller}", memberId, caller);

    var accounts = MockData.AccountsFor(memberId);
    return Results.Ok(new
    {
        memberId,
        servedBy = podName,
        callerIp = caller,
        accounts
    });
});

app.MapGet("/api/members/{memberId:int}/cards", (int memberId, HttpContext ctx) =>
{
    var caller = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    logger.LogInformation("cards lookup member={MemberId} caller={Caller}", memberId, caller);
    return Results.Ok(new { memberId, servedBy = podName, callerIp = caller, cards = MockData.CardsFor(memberId) });
});

app.Run();

static class MockData
{
    // Deterministic pseudo-random so the same member always shows the same balances.
    static Random Rng(int seed) => new Random(seed * 7919);

    public static object[] AccountsFor(int memberId)
    {
        var r = Rng(memberId);
        var list = new List<object>
        {
            new {
                accountId = $"{memberId:D6}-S01",
                type = "Share Savings",
                balance = Math.Round(r.NextDouble() * 18000 + 25, 2),
                openedOn = DateTime.Today.AddDays(-r.Next(400, 6000)).ToString("yyyy-MM-dd"),
                status = "Open"
            },
            new {
                accountId = $"{memberId:D6}-D01",
                type = "Checking",
                balance = Math.Round(r.NextDouble() * 6500 + 10, 2),
                openedOn = DateTime.Today.AddDays(-r.Next(200, 5000)).ToString("yyyy-MM-dd"),
                status = "Open"
            }
        };

        if (r.Next(0, 3) == 0)
        {
            list.Add(new {
                accountId = $"{memberId:D6}-L01",
                type = "Auto Loan",
                balance = -Math.Round(r.NextDouble() * 32000 + 1500, 2),
                openedOn = DateTime.Today.AddDays(-r.Next(30, 1800)).ToString("yyyy-MM-dd"),
                status = "Current"
            });
        }

        if (r.Next(0, 4) == 0)
        {
            list.Add(new {
                accountId = $"{memberId:D6}-C01",
                type = "Share Certificate (12 mo)",
                balance = Math.Round(r.NextDouble() * 25000 + 1000, 2),
                openedOn = DateTime.Today.AddDays(-r.Next(10, 360)).ToString("yyyy-MM-dd"),
                status = "Open"
            });
        }

        return list.ToArray();
    }

    public static object[] CardsFor(int memberId)
    {
        var r = Rng(memberId + 1);
        return new object[]
        {
            new { last4 = r.Next(1000, 9999).ToString(), network = "Visa", product = "Debit", status = r.Next(0, 10) == 0 ? "Blocked" : "Active" }
        };
    }
}
