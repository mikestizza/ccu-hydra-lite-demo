namespace HydraWeb.Services;

/// <summary>
/// Everything the UI shows about "where am I running". Pulled from environment
/// variables that the Kubernetes manifests set, so the same image behaves
/// differently in prod vs an ephemeral branch namespace.
/// </summary>
public sealed class AppInfo
{
    public string Environment { get; }
    public string Namespace { get; }
    public string Pod { get; }
    public string Node { get; }
    public string Cluster { get; }
    public string Version { get; }
    public string GitSha { get; }
    public string Accent { get; }
    public string Banner { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public AppInfo(IConfiguration cfg)
    {
        Environment = cfg["Hydra:Environment"] ?? "local";
        Namespace = System.Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "n/a";
        Pod = System.Environment.GetEnvironmentVariable("HOSTNAME") ?? System.Environment.MachineName;
        Node = System.Environment.GetEnvironmentVariable("NODE_NAME") ?? "n/a";
        // NKP names worker nodes "<cluster>-md-0-...", so the cluster is the part before "-md-".
        // CLUSTER_NAME, if set, wins.
        Cluster = System.Environment.GetEnvironmentVariable("CLUSTER_NAME")
                  ?? (Node.Contains("-md-") ? Node[..Node.IndexOf("-md-")] : Node);
        Version = cfg["APP_VERSION"] ?? "dev";
        GitSha = cfg["GIT_SHA"] ?? "local";
        Accent = cfg["Hydra:Accent"] ?? "#0f2a5a";
        Banner = cfg["Hydra:Banner"] ?? "";
    }

    public string Uptime
    {
        get
        {
            var t = DateTimeOffset.UtcNow - StartedAt;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
        }
    }

    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        + " on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription;
}
