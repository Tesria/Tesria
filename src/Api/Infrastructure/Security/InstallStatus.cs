namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// What the <c>init</c> service found about this install (dev-plan 25.1),
/// from the non-secret files it leaves in <c>/run/tesria/status</c>: whether
/// Tesria made the backup key, and which secrets are still the example
/// values from an old <c>.env.example</c>. Never a secret itself; the app
/// has no mount that holds one it was not already given.
///
/// Read on every call: the files are a few bytes, and init rewrites them
/// whenever the stack is started, without restarting the app.
/// </summary>
public sealed class InstallStatus(IConfiguration config)
{
    private string Directory =>
        config["Install:StatusDirectory"] ?? Path.Combine(SecretFiles.DefaultDirectory, "status");

    /// <summary>True when init generated the backup key, so no person has seen it yet.</summary>
    public bool BackupKeyGenerated => Lines("backup-key-source").FirstOrDefault() == "generated";

    /// <summary>The <c>.env</c> settings still holding a <c>change-me</c> value.</summary>
    public IReadOnlyList<string> PlaceholderSettings => Lines("placeholders");

    private List<string> Lines(string name)
    {
        try
        {
            var path = Path.Combine(Directory, name);
            return File.Exists(path)
                ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
