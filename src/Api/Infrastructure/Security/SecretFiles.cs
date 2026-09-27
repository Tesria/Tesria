using Npgsql;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Secrets read from the files the <c>init</c> service writes (dev-plan 25.1).
///
/// Until 0.8.0 every secret reached the app as an environment variable that
/// Compose built from <c>.env</c>. Now <c>init</c> generates any that are
/// not set and leaves each in its own volume, mounted only where it was
/// before: <c>/run/tesria/&lt;name&gt;/value</c>. This fills the settings the
/// rest of the app already reads (<c>ConnectionStrings:Default</c>,
/// <c>ConnectionStrings:App</c>, <c>Collab:Secret</c>,
/// <c>Pdf:SharedSecret</c>) from those files.
///
/// **A setting that is already configured wins.** Only an empty one is
/// filled, so a connection string set in the environment, as the scratch
/// instance and <c>dotnet run</c> do, behaves exactly as before, and a
/// machine with no <c>/run/tesria</c> sees no change at all.
///
/// **The owner's connection needs <c>Database:OwnerUser</c>**, which only the
/// <c>migrate</c> service is given, as only it mounts the owner password.
/// </summary>
public static class SecretFiles
{
    public const string DefaultDirectory = "/run/tesria";

    public static IConfigurationManager AddTesriaSecretFiles(this IConfigurationManager config)
    {
        var values = Resolve(config, config["Secrets:Directory"] ?? DefaultDirectory);
        if (values.Count > 0) config.AddInMemoryCollection(values);
        return config;
    }

    public static Dictionary<string, string?> Resolve(IConfiguration config, string directory)
    {
        var result = new Dictionary<string, string?>();

        void Fill(string key, string name)
        {
            if (string.IsNullOrEmpty(config[key]) && Read(directory, name) is { } value)
                result[key] = value;
        }
        Fill("Collab:Secret", "collab-secret");
        Fill("Pdf:SharedSecret", "pdf-secret");

        // Built, not concatenated: a password set in .env may hold a ';' or a
        // quote, which a hand-built string would split or break.
        string Connection(string user, string password) => new NpgsqlConnectionStringBuilder
        {
            Host = config["Database:Host"] ?? "db",
            Port = int.TryParse(config["Database:Port"], out var port) ? port : 5432,
            Database = config["Database:Name"] ?? "tesria",
            Username = user,
            Password = password,
        }.ConnectionString;

        if (string.IsNullOrEmpty(config.GetConnectionString("Default"))
            && config["Database:OwnerUser"] is { Length: > 0 } owner
            && Read(directory, "postgres-password") is { } ownerPassword)
            result["ConnectionStrings:Default"] = Connection(owner, ownerPassword);

        if (string.IsNullOrEmpty(config.GetConnectionString("App"))
            && Read(directory, "app-db-password") is { } appPassword)
            result["ConnectionStrings:App"] = Connection(config["Database:AppUser"] ?? "tesria_app", appPassword);

        return result;
    }

    /// <summary>
    /// The value, or null when there is none this process may read. A
    /// trailing newline is dropped, so a file written by hand works; nothing
    /// else is, because Postgres was given the value exactly as stored.
    /// </summary>
    private static string? Read(string directory, string name)
    {
        var path = Path.Combine(directory, name, "value");
        try
        {
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path).TrimEnd('\r', '\n');
            return value.Length > 0 ? value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
