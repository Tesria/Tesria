using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Versioning;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Administration, About (the owner, 2026-09-24): which Tesria this is,
/// everything it is built from with each license, and whether any of it has
/// a known vulnerability, so that when a new one is announced an
/// administrator can see at once whether their instance is exposed.
///
/// <para>The list is the manifest <c>scripts/deps/build-manifest.mjs</c>
/// writes at release time, embedded in the app. Checking it asks OSV.dev
/// (the open vulnerability database behind GitHub's and npm's advisories)
/// about every package, and only when an administrator presses the button:
/// the check sends the package names and versions, and this server's
/// address, to an outside service, so it is never done unasked. The result
/// is kept, with when it was made and by whom.</para>
/// </summary>
public static class AboutEndpoints
{
    /// <param name="Note">For a container image: what it is for (runs the app, builds it, optional, testing only).</param>
    public record Dependency(string Ecosystem, string Name, string Version, string License, string Component, bool Direct, string? Url, string? Note = null);
    private record Manifest(int Format, List<Dependency> Dependencies);

    public record Vulnerability(string Id, string? Summary, string? Severity, IReadOnlyList<string> Aliases, string Url);
    public record Affected(string Ecosystem, string Name, string Version, IReadOnlyList<string> Components, IReadOnlyList<Vulnerability> Vulnerabilities);
    public record CheckResult(DateTimeOffset At, string? ByName, int Checked, IReadOnlyList<Affected> Affected, string? Error);

    public record AboutResponse(
        string Version, string? PreviousVersion, DateTimeOffset? VersionChangedAt,
        IReadOnlyList<Dependency> Dependencies, CheckResult? LastCheck);

    public static IEndpointRouteBuilder MapAboutEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/about").WithTags("Admin").RequireAuthorization();
        group.MapGet("/", Get).RequirePermission(InstancePermissions.DashboardView);
        group.MapGet("/notices", Notices).RequirePermission(InstancePermissions.DashboardView);
        group.MapPost("/check", Check).RequirePermission(InstancePermissions.SecurityView);
        return routes;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The embedded manifest, read once.</summary>
    public static readonly Lazy<IReadOnlyList<Dependency>> Dependencies = new(() =>
    {
        using var stream = typeof(AboutEndpoints).Assembly.GetManifestResourceStream("Tesria.About.dependencies.json");
        if (stream is null) return [];
        return JsonSerializer.Deserialize<Manifest>(stream, Json)?.Dependencies ?? [];
    });

    private static async Task<IResult> Get(ISiteSettingsService settings, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return Results.Ok(new AboutResponse(
            AppVersion.Current, s.PreviousVersion, s.PreviousVersion is null ? null : s.VersionChangedAt,
            Dependencies.Value, LastCheck(s.DependencyCheckJson)));
    }

    private static CheckResult? LastCheck(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<CheckResult>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static IResult Notices()
    {
        var stream = typeof(AboutEndpoints).Assembly.GetManifestResourceStream("Tesria.About.notices.txt");
        return stream is null ? Results.NotFound() : Results.Stream(stream, "text/plain; charset=utf-8");
    }

    private static async Task<IResult> Check(
        IOsvClient osv, ISiteSettingsService settings, CurrentUser current, AppDbContext db,
        IAuditLogger audit, CancellationToken ct)
    {
        // Container images are not packages OSV knows by name; the rest are
        // asked once each, however many components use them.
        var packages = Dependencies.Value
            .Where(d => d.Ecosystem is "npm" or "NuGet")
            .GroupBy(d => (d.Ecosystem, d.Name, d.Version))
            .Select(g => new { g.Key.Ecosystem, g.Key.Name, g.Key.Version, Components = g.Select(d => d.Component).Distinct().Order().ToList() })
            .ToList();

        var id = current.RequireId();
        var byName = await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        CheckResult result;
        try
        {
            var found = await osv.QueryAsync([.. packages.Select(p => new OsvPackage(p.Ecosystem, p.Name, p.Version))], ct);
            var affected = packages
                .Select((p, i) => new Affected(p.Ecosystem, p.Name, p.Version, p.Components, found[i]))
                .Where(a => a.Vulnerabilities.Count > 0)
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result = new CheckResult(DateTimeOffset.UtcNow, byName, packages.Count, affected, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OsvException)
        {
            // Offline, blocked by a firewall, or OSV.dev down: said plainly,
            // and the previous result stays as it was.
            return Results.Problem(
                detail: "OSV.dev could not be reached, so nothing was checked. Is this server allowed to reach api.osv.dev?",
                statusCode: StatusCodes.Status502BadGateway);
        }

        await settings.UpdateAsync(s => s.DependencyCheckJson = JsonSerializer.Serialize(result, Json), id, ct);
        audit.Record("dependencies.checked", "instance", null,
            new { result.Checked, Vulnerable = result.Affected.Count, Ids = result.Affected.SelectMany(a => a.Vulnerabilities.Select(v => v.Id)).Distinct().ToList() });
        await db.SaveChangesAsync(ct);
        return Results.Ok(result);
    }
}

public record OsvPackage(string Ecosystem, string Name, string Version);

public sealed class OsvException(string message) : Exception(message);

/// <summary>Asks OSV.dev which of these packages have known vulnerabilities; one list per package, in order.</summary>
public interface IOsvClient
{
    Task<IReadOnlyList<IReadOnlyList<AboutEndpoints.Vulnerability>>> QueryAsync(IReadOnlyList<OsvPackage> packages, CancellationToken ct);
}

/// <summary>
/// OSV.dev's batch query, then the details of each vulnerability it names.
/// The batch answers with ids only; the summary, severity and CVE aliases
/// need one request per vulnerability, which a healthy instance keeps short.
/// </summary>
public sealed class OsvClient(IHttpClientFactory http) : IOsvClient
{
    public const string HttpClientName = "osv";
    private const int BatchSize = 500;
    private const int MaxDetails = 200;

    private record Query([property: JsonPropertyName("package")] Pkg Package, [property: JsonPropertyName("version")] string Version);
    private record Pkg([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("ecosystem")] string Ecosystem);
    private record BatchResponse([property: JsonPropertyName("results")] List<BatchResult>? Results);
    private record BatchResult([property: JsonPropertyName("vulns")] List<VulnRef>? Vulns);
    private record VulnRef([property: JsonPropertyName("id")] string Id);

    public async Task<IReadOnlyList<IReadOnlyList<AboutEndpoints.Vulnerability>>> QueryAsync(
        IReadOnlyList<OsvPackage> packages, CancellationToken ct)
    {
        var client = http.CreateClient(HttpClientName);
        var ids = new List<List<string>>();
        for (var start = 0; start < packages.Count; start += BatchSize)
        {
            var batch = packages.Skip(start).Take(BatchSize)
                .Select(p => new Query(new Pkg(p.Name, p.Ecosystem), p.Version)).ToList();
            using var res = await client.PostAsJsonAsync("v1/querybatch", new { queries = batch }, ct);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<BatchResponse>(ct);
            var results = body?.Results ?? throw new OsvException("OSV.dev answered without results.");
            if (results.Count != batch.Count) throw new OsvException("OSV.dev answered for a different number of packages.");
            ids.AddRange(results.Select(r => (r.Vulns ?? []).Select(v => v.Id).Distinct().ToList()));
        }

        var details = new Dictionary<string, AboutEndpoints.Vulnerability>();
        foreach (var id in ids.SelectMany(x => x).Distinct().Take(MaxDetails))
            details[id] = await DetailAsync(client, id, ct);

        return [.. ids.Select(list => (IReadOnlyList<AboutEndpoints.Vulnerability>)
            [.. list.Select(id => details.TryGetValue(id, out var d) ? d : new AboutEndpoints.Vulnerability(id, null, null, [], Link(id)))])];
    }

    private static async Task<AboutEndpoints.Vulnerability> DetailAsync(HttpClient client, string id, CancellationToken ct)
    {
        using var res = await client.GetAsync($"v1/vulns/{Uri.EscapeDataString(id)}", ct);
        if (!res.IsSuccessStatusCode) return new(id, null, null, [], Link(id));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var summary = Str(root, "summary") ?? Str(root, "details")?.Split('\n')[0];
        // GitHub's advisories carry a plain severity word; that is the one worth showing.
        string? severity = root.TryGetProperty("database_specific", out var db) ? Str(db, "severity") : null;
        var aliases = root.TryGetProperty("aliases", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
        return new(id, summary is { Length: > 300 } ? summary[..300] : summary, severity, aliases, Link(id));
    }

    private static string Link(string id) => $"https://osv.dev/vulnerability/{Uri.EscapeDataString(id)}";
}
