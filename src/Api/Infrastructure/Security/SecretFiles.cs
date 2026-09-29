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
///
/// **A database password filled from a file is read again from it** (0.8.1
/// QA, T1-029). Changing <c>APP_DB_PASSWORD</c> in <c>.env</c> makes
/// <c>init</c> rewrite the file while every other service keeps running,
/// so a value read once at startup goes stale: <c>migrate</c> never gave the
/// role the new password, and the app, on its next restart, had one the
/// database refused. So each filled connection string also records its file
/// (<see cref="AppPasswordFileKey"/>, <see cref="OwnerPasswordFileKey"/>):
/// <c>migrate</c> watches it and updates the role, and the app reads it for
/// each new database connection (<see cref="WithCurrentPassword"/>).
/// </summary>
public static class SecretFiles
{
    public const string DefaultDirectory = "/run/tesria";

    /// <summary>Set when <c>ConnectionStrings:App</c>'s password came from this file.</summary>
    public const string AppPasswordFileKey = "Secrets:AppPasswordFile";

    /// <summary>Set when <c>ConnectionStrings:Default</c>'s password came from this file.</summary>
    public const string OwnerPasswordFileKey = "Secrets:OwnerPasswordFile";

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
        {
            result["ConnectionStrings:Default"] = Connection(owner, ownerPassword);
            result[OwnerPasswordFileKey] = ValuePath(directory, "postgres-password");
        }

        if (string.IsNullOrEmpty(config.GetConnectionString("App"))
            && Read(directory, "app-db-password") is { } appPassword)
        {
            result["ConnectionStrings:App"] = Connection(config["Database:AppUser"] ?? "tesria_app", appPassword);
            result[AppPasswordFileKey] = ValuePath(directory, "app-db-password");
        }

        return result;
    }

    /// <summary>
    /// The connection string with the password its file holds now, or
    /// unchanged when there is no file or it cannot be read (the value read
    /// at startup is then the best there is).
    /// </summary>
    public static string WithCurrentPassword(string connection, string? passwordFile)
    {
        if (string.IsNullOrEmpty(passwordFile) || ReadPath(passwordFile) is not { } current) return connection;
        var b = new NpgsqlConnectionStringBuilder(connection);
        if (b.Password == current) return connection;
        b.Password = current;
        return b.ConnectionString;
    }

    /// <summary>
    /// Makes every new physical connection of a data source use the
    /// password the file holds at that moment, falling back to the one in
    /// the connection string when the file cannot be read. Connections
    /// already open stay open: Postgres checks a password only at sign-in.
    /// </summary>
    public static void ReadPasswordFromFile(NpgsqlDataSourceBuilder builder, string passwordFile)
    {
        // Npgsql refuses a password provider beside a password in the
        // connection string, so the startup value moves into the fallback.
        var fallback = builder.ConnectionStringBuilder.Password;
        builder.ConnectionStringBuilder.Password = null;
        builder.UsePasswordProvider(
            _ => ReadPath(passwordFile) ?? fallback ?? "",
            (_, _) => ValueTask.FromResult(ReadPath(passwordFile) ?? fallback ?? ""));
    }

    private static string ValuePath(string directory, string name) => Path.Combine(directory, name, "value");

    /// <summary>
    /// The value, or null when there is none this process may read. A
    /// trailing newline is dropped, so a file written by hand works; nothing
    /// else is, because Postgres was given the value exactly as stored.
    /// </summary>
    private static string? Read(string directory, string name) => ReadPath(ValuePath(directory, name));

    private static string? ReadPath(string path)
    {
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
