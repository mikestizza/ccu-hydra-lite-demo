using HydraWeb.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AppInfo>();
builder.Services.AddSingleton<HydraDb>();
builder.Services.AddHostedService<JobRunner>();
builder.Services.AddHttpClient<ColbyClient>(http =>
{
    var baseUrl = builder.Configuration["ColbyApi:BaseUrl"] ?? "http://localhost:5081";
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Behind Traefik / a load balancer we want the real scheme and client IP.
var fwd = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
fwd.KnownNetworks.Clear(); // trust the in-cluster ingress controller
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// Liveness: process is up. Readiness: database reachable.
var db = app.Services.GetRequiredService<HydraDb>();
var info = app.Services.GetRequiredService<AppInfo>();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", pod = info.Pod, version = info.Version }));
app.MapGet("/readyz", async (CancellationToken ct) =>
    await db.PingAsync(ct)
        ? Results.Ok(new { status = "ready", pod = info.Pod })
        : Results.StatusCode(503));

// Create database, schema and seed data in the background so the pod can
// report liveness immediately while SQL Server finishes starting.
_ = Task.Run(() => db.InitializeAsync(app.Lifetime.ApplicationStopping));

app.Logger.LogInformation("Hydra-lite starting. env={Env} version={Version} sha={Sha} colby={Colby}",
    info.Environment, info.Version, info.GitSha, builder.Configuration["ColbyApi:BaseUrl"]);

app.Run();
