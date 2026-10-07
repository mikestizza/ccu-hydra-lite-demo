using Microsoft.Data.SqlClient;

namespace HydraWeb.Services;

public record Member(int MemberId, string FirstName, string LastName, string Email, string Phone, string Branch, DateTime JoinedOn, string Status)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record LookupEntry(int Id, int MemberId, string MemberName, DateTime LookedUpAt, string Pod);

public record JobRun(int Id, string JobName, DateTime StartedAt, int DurationMs, string Status, string Pod);

/// <summary>
/// Thin data access over SQL Server using Microsoft.Data.SqlClient (the managed
/// driver, no native client needed in the image). On startup it creates the
/// database, tables and seed data if they do not exist, so a brand new
/// ephemeral namespace comes up with usable data and no manual steps.
/// </summary>
public sealed class HydraDb
{
    private readonly string _connString;
    private readonly ILogger<HydraDb> _log;

    public HydraDb(IConfiguration cfg, ILogger<HydraDb> log)
    {
        _connString = ResolveConnectionString(cfg);
        _log = log;
    }

    /// <summary>
    /// Precedence: an explicit ConnectionStrings:Hydra wins. Otherwise compose one
    /// from Hydra:DbHost (the Kubernetes service name) and MSSQL_SA_PASSWORD, which
    /// is the single key operators put in the hydra-secret Secret.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration cfg)
    {
        var explicitCs = cfg.GetConnectionString("Hydra");
        if (!string.IsNullOrWhiteSpace(explicitCs)) return explicitCs;

        var pw = cfg["MSSQL_SA_PASSWORD"];
        if (string.IsNullOrWhiteSpace(pw))
            throw new InvalidOperationException("Set ConnectionStrings__Hydra or MSSQL_SA_PASSWORD");

        var host = cfg["Hydra:DbHost"] ?? "hydra-db";
        var db   = cfg["Hydra:DbName"] ?? "Hydra";
        return new SqlConnectionStringBuilder
        {
            DataSource = $"{host},1433",
            InitialCatalog = db,
            UserID = "sa",
            Password = pw,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            ConnectTimeout = 15
        }.ConnectionString;
    }

    private SqlConnection Open()
    {
        var c = new SqlConnection(_connString);
        c.Open();
        return c;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await using var c = new SqlConnection(_connString);
            await c.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT 1", c);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch { return false; }
    }

    public string ServerInfo()
    {
        try
        {
            using var c = Open();
            using var cmd = new SqlCommand("SELECT @@SERVERNAME + ' / ' + CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) + ' / ' + DB_NAME()", c);
            return cmd.ExecuteScalar()?.ToString() ?? "unknown";
        }
        catch (Exception ex) { return "unavailable: " + ex.Message; }
    }

    // ---------- startup ----------

    public async Task InitializeAsync(CancellationToken ct)
    {
        // SQL Server in a fresh pod can take 20-40s to accept connections. Retry.
        var builder = new SqlConnectionStringBuilder(_connString);
        var targetDb = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "Hydra" : builder.InitialCatalog;
        builder.InitialCatalog = "master";

        for (var attempt = 1; attempt <= 30 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await using (var master = new SqlConnection(builder.ConnectionString))
                {
                    await master.OpenAsync(ct);
                    await using var create = new SqlCommand(
                        $"IF DB_ID(N'{targetDb}') IS NULL CREATE DATABASE [{targetDb}]", master);
                    await create.ExecuteNonQueryAsync(ct);
                }

                await using (var c = new SqlConnection(_connString))
                {
                    await c.OpenAsync(ct);
                    await using var schema = new SqlCommand(Schema, c);
                    await schema.ExecuteNonQueryAsync(ct);

                    await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.Members", c);
                    var n = (int)(await count.ExecuteScalarAsync(ct) ?? 0);
                    if (n == 0)
                    {
                        await using var seed = new SqlCommand(SeedData.MembersInsert, c);
                        await seed.ExecuteNonQueryAsync(ct);
                        _log.LogInformation("Seeded Members table");
                    }
                }

                _log.LogInformation("Database {Db} ready after {Attempt} attempt(s)", targetDb, attempt);
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                _log.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private const string Schema = """
        IF OBJECT_ID('dbo.Members') IS NULL
        CREATE TABLE dbo.Members (
            MemberId  INT NOT NULL PRIMARY KEY,
            FirstName NVARCHAR(50) NOT NULL,
            LastName  NVARCHAR(50) NOT NULL,
            Email     NVARCHAR(120) NOT NULL,
            Phone     NVARCHAR(20) NOT NULL,
            Branch    NVARCHAR(40) NOT NULL,
            JoinedOn  DATE NOT NULL,
            Status    NVARCHAR(20) NOT NULL
        );
        IF OBJECT_ID('dbo.LookupLog') IS NULL
        CREATE TABLE dbo.LookupLog (
            Id         INT IDENTITY(1,1) PRIMARY KEY,
            MemberId   INT NOT NULL,
            LookedUpAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            Pod        NVARCHAR(80) NOT NULL
        );
        IF OBJECT_ID('dbo.JobRuns') IS NULL
        CREATE TABLE dbo.JobRuns (
            Id         INT IDENTITY(1,1) PRIMARY KEY,
            JobName    NVARCHAR(80) NOT NULL,
            StartedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            DurationMs INT NOT NULL,
            Status     NVARCHAR(20) NOT NULL,
            Pod        NVARCHAR(80) NOT NULL
        );
        """;

    // ---------- members ----------

    public List<Member> SearchMembers(string? q, int take = 50)
    {
        using var c = Open();
        var sql = """
            SELECT TOP (@take) MemberId, FirstName, LastName, Email, Phone, Branch, JoinedOn, Status
            FROM dbo.Members
            WHERE (@q = '' OR FirstName LIKE '%' + @q + '%' OR LastName LIKE '%' + @q + '%'
                   OR Email LIKE '%' + @q + '%' OR CAST(MemberId AS nvarchar(20)) LIKE @q + '%')
            ORDER BY LastName, FirstName
            """;
        using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@q", (q ?? "").Trim());
        cmd.Parameters.AddWithValue("@take", take);
        using var r = cmd.ExecuteReader();
        var list = new List<Member>();
        while (r.Read()) list.Add(ReadMember(r));
        return list;
    }

    public Member? GetMember(int id)
    {
        using var c = Open();
        using var cmd = new SqlCommand(
            "SELECT MemberId, FirstName, LastName, Email, Phone, Branch, JoinedOn, Status FROM dbo.Members WHERE MemberId = @id", c);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMember(r) : null;
    }

    private static Member ReadMember(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetString(4), r.GetString(5), r.GetDateTime(6), r.GetString(7));

    public void LogLookup(int memberId, string pod)
    {
        using var c = Open();
        using var cmd = new SqlCommand("INSERT INTO dbo.LookupLog (MemberId, Pod) VALUES (@m, @p)", c);
        cmd.Parameters.AddWithValue("@m", memberId);
        cmd.Parameters.AddWithValue("@p", pod);
        cmd.ExecuteNonQuery();
    }

    public List<LookupEntry> RecentLookups(int take = 10)
    {
        using var c = Open();
        using var cmd = new SqlCommand("""
            SELECT TOP (@take) l.Id, l.MemberId, m.FirstName + ' ' + m.LastName, l.LookedUpAt, l.Pod
            FROM dbo.LookupLog l JOIN dbo.Members m ON m.MemberId = l.MemberId
            ORDER BY l.Id DESC
            """, c);
        cmd.Parameters.AddWithValue("@take", take);
        using var r = cmd.ExecuteReader();
        var list = new List<LookupEntry>();
        while (r.Read()) list.Add(new LookupEntry(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetDateTime(3), r.GetString(4)));
        return list;
    }

    public (int members, int lookupsToday, int jobsToday) Stats()
    {
        using var c = Open();
        using var cmd = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.Members),
              (SELECT COUNT(*) FROM dbo.LookupLog WHERE LookedUpAt >= CAST(SYSUTCDATETIME() AS date)),
              (SELECT COUNT(*) FROM dbo.JobRuns  WHERE StartedAt  >= CAST(SYSUTCDATETIME() AS date))
            """, c);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
    }

    // ---------- jobs ----------

    public void RecordJob(string name, int durationMs, string status, string pod)
    {
        using var c = Open();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.JobRuns (JobName, DurationMs, Status, Pod) VALUES (@n, @d, @s, @p)", c);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@d", durationMs);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@p", pod);
        cmd.ExecuteNonQuery();
    }

    public List<JobRun> RecentJobs(int take = 25)
    {
        using var c = Open();
        using var cmd = new SqlCommand(
            "SELECT TOP (@take) Id, JobName, StartedAt, DurationMs, Status, Pod FROM dbo.JobRuns ORDER BY Id DESC", c);
        cmd.Parameters.AddWithValue("@take", take);
        using var r = cmd.ExecuteReader();
        var list = new List<JobRun>();
        while (r.Read()) list.Add(new JobRun(r.GetInt32(0), r.GetString(1), r.GetDateTime(2), r.GetInt32(3), r.GetString(4), r.GetString(5)));
        return list;
    }
}
