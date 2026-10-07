using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydraWeb.Services;

public record ColbyAccount(string AccountId, string Type, decimal Balance, string OpenedOn, string Status);
public record ColbyAccountsResponse(int MemberId, string ServedBy, string CallerIp, ColbyAccount[] Accounts);

public record ColbyResult(bool Ok, string Target, long ElapsedMs, ColbyAccountsResponse? Data, string? Error);

/// <summary>
/// Calls the in-house "Colby API" over the cluster network. The base URL is a
/// Kubernetes service name (http://colby-api), so this traffic never leaves
/// the cluster and never touches the external load balancer.
/// </summary>
public sealed class ColbyClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ColbyClient> _log;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? "(unset)";

    public ColbyClient(HttpClient http, ILogger<ColbyClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<ColbyResult> GetAccountsAsync(int memberId, CancellationToken ct = default)
    {
        var path = $"/api/members/{memberId}/accounts";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await _http.GetAsync(path, ct);
            sw.Stop();
            if (!resp.IsSuccessStatusCode)
                return new ColbyResult(false, BaseUrl + path, sw.ElapsedMilliseconds, null, $"HTTP {(int)resp.StatusCode}");

            var data = await resp.Content.ReadFromJsonAsync<ColbyAccountsResponse>(JsonOpts, ct);
            return new ColbyResult(true, BaseUrl + path, sw.ElapsedMilliseconds, data, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogWarning("Colby API call failed: {Message}", ex.Message);
            return new ColbyResult(false, BaseUrl + path, sw.ElapsedMilliseconds, null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/healthz", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
