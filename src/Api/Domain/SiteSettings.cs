namespace Tesria.Api.Domain;

/// <summary>
/// Instance-wide configuration an administrator can change at runtime, as
/// opposed to the deploy-time configuration in environment variables and
/// appsettings (connection strings, OIDC, the collab secret) which stays
/// where it is — those are secrets and topology, set before the app starts.
///
/// Exactly one row, keyed by <see cref="SingletonId"/>. Typed columns rather
/// than a key/value table: EF validates them, a migration records every shape
/// change, and the admin UI can bind to them without parsing strings.
/// </summary>
public class SiteSettings
{
    /// <summary>
    /// The fixed primary key of the single row. A constant rather than a
    /// generated id so "get the settings" is a primary-key lookup and a second
    /// row cannot be created by accident.
    /// </summary>
    public static readonly Guid SingletonId = new("5171e5e7-0000-4000-8000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>Shown in the UI and used as the sender name for outbound email.</summary>
    public string InstanceName { get; set; } = "Tesria";

    /// <summary>
    /// The address links in email point at, e.g. <c>https://wiki.example.com</c>.
    /// Null means use the deploy-time <c>Site:BaseUrl</c> (derived from
    /// <c>DOMAIN</c>); set here when the instance is reached at a different
    /// name than the one Caddy was configured with.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Whether anyone who can reach <c>/register</c> may create an account.
    /// Defaults to true, which is the behaviour before this setting existed.
    /// Registration on a completely empty instance ignores this — see
    /// AuthEndpoints.Register — so an operator cannot lock themselves out of a
    /// fresh install by turning it off before the first account exists.
    /// </summary>
    public bool AllowPublicRegistration { get; set; } = true;

    /// <summary>
    /// Instance-wide kill switch for anonymous read access (dev-plan Phase 5).
    /// Off by default: exposing content to the internet must be a deliberate
    /// act, and the per-space toggle is only offered when this is on.
    /// </summary>
    public bool AllowPublicSpaces { get; set; }

    /// <summary>Whether outbound email is configured and should be attempted.</summary>
    public bool EmailEnabled { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }

    /// <summary>
    /// The SMTP password, encrypted with ASP.NET Data Protection (whose keys
    /// already live in this database, so a restore stays self-consistent).
    /// Never leaves the server: the API reports only whether one is set.
    /// </summary>
    public string? SmtpPasswordProtected { get; set; }

    public string? SmtpFromAddress { get; set; }
    public SmtpTlsMode SmtpTls { get; set; } = SmtpTlsMode.StartTls;

    /// <summary>Reserved for dev-plan 3.5; stored here so the admin UI has one home.</summary>
    /// <summary>
    /// Hosts whose pages may be framed by an <c>embed</c> block, one per
    /// line (dev-plan Phase 7 Wave E). A leading <c>.</c> matches subdomains.
    ///
    /// This is the *only* thing standing between an author and an arbitrary
    /// iframe on everyone else's page, so it is enforced twice: the resolve
    /// endpoint refuses a URL that is not on it, and the CSP's `frame-src` is
    /// built from it, so even a client-side bug cannot frame an off-list host.
    ///
    /// The default is the small set of well-known media and design hosts an
    /// embed block exists for. Emptying it turns embeds off entirely.
    /// </summary>
    public string EmbedAllowlist { get; set; } = string.Join('\n', DefaultEmbedAllowlist);

    public static readonly string[] DefaultEmbedAllowlist =
    [
        ".youtube.com", "youtu.be", ".youtube-nocookie.com",
        ".vimeo.com", ".loom.com",
        ".figma.com", ".miro.com", ".codepen.io",
        "docs.google.com", "drive.google.com",
    ];

    public bool RequireTotpForAdmins { get; set; }

    // --- Brute-force protection (dev-plan 3.2). Every limiter is tunable
    // here so an operator under attack can tighten without a redeploy, and
    // one whose users are behind a shared NAT can loosen the per-address
    // limits without turning protection off.

    /// <summary>Sign-in, registration and recovery attempts allowed per address per minute.</summary>
    public int LoginRateLimitPerMinute { get; set; } = 10;

    /// <summary>Requests per address per minute for callers with no session (Phase 5 relies on this).</summary>
    public int AnonymousRateLimitPerMinute { get; set; } = 300;

    /// <summary>API tokens one account may mint per hour.</summary>
    public int TokenMintLimitPerHour { get; set; } = 20;

    /// <summary>Consecutive failed sign-ins before an account is locked.</summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>
    /// First lockout length; each further failure doubles it up to
    /// <see cref="LockoutMaxSeconds"/>. Never permanent: a permanent lockout
    /// would let anyone lock anyone out by guessing at their address.
    /// </summary>
    public int LockoutBaseSeconds { get; set; } = 60;

    public int LockoutMaxSeconds { get; set; } = 900;

    // --- Backup retention (dev-plan 9.1). Read by the backup sidecars, which
    // apply it; the app only stores it and previews its effect. A backup is
    // removed only when it is outside both limits: not among the newest
    // BackupKeepCount, and older than BackupKeepDays.

    /// <summary>False keeps every backup forever.</summary>
    public bool BackupRetentionEnabled { get; set; } = true;

    public int BackupKeepCount { get; set; } = 3;

    public int BackupKeepDays { get; set; } = 14;

    /// <summary>
    /// Null means no one has set the policy yet, and the sidecars remove
    /// nothing. Set at startup from <c>BACKUP_RETENTION_DAYS</c> (see
    /// BackupPolicySeed) and whenever an administrator saves it.
    /// </summary>
    public DateTimeOffset? BackupPolicyChangedAt { get; set; }

    public Guid? BackupPolicyChangedById { get; set; }

    /// <summary>
    /// When the owner finished first-run setup (dev-plan 10.2). Null means the
    /// wizard is still to be done; an instance upgraded from before it existed
    /// is stamped by <see cref="Infrastructure.Auth.OwnerSeed"/> instead.
    /// </summary>
    public DateTimeOffset? SetupCompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}

public enum SmtpTlsMode
{
    None = 0,
    StartTls = 1,
    SslOnConnect = 2,
}
