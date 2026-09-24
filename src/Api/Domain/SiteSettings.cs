namespace Tesria.Api.Domain;

/// <summary>
/// Instance-wide configuration an administrator can change at runtime, as
/// opposed to the deploy-time configuration in environment variables and
/// appsettings (connection strings, OIDC, the collab secret) which stays
/// where it is: those are secrets and topology, set before the app starts.
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
    /// Defaults to true, which is the behavior before this setting existed.
    /// Registration on a completely empty instance ignores this, see
    /// AuthEndpoints.Register, so an operator cannot lock themselves out of a
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

    // --- Mail providers (dev-plan Phase 18).

    /// <summary>
    /// The preset last chosen in the email settings, such as <c>gmail</c>
    /// (<see cref="Infrastructure.Email.MailProviders"/>); null for Other.
    /// Only what the form shows: the host, port and encryption above are
    /// what is used.
    /// </summary>
    public string? SmtpProvider { get; set; }

    /// <summary>How Tesria signs in to the mail server: a password, or a provider's sign-in (18.2, 18.3).</summary>
    public MailSignIn SmtpSignIn { get; set; } = MailSignIn.Password;

    /// <summary>
    /// The administrator's own app registration with Microsoft (18.2). Every
    /// instance has its own address, so there is no shared one. The secret is
    /// protected like the SMTP password and never leaves the server.
    /// </summary>
    public string? MicrosoftClientId { get; set; }
    public string? MicrosoftClientSecretProtected { get; set; }
    /// <summary>The directory (tenant) the app is registered in; null is <c>common</c>, any account.</summary>
    public string? MicrosoftTenant { get; set; }

    /// <summary>The administrator's own Google Cloud OAuth client (18.3).</summary>
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecretProtected { get; set; }

    /// <summary>
    /// The refresh token from the provider's sign-in: it can send mail as
    /// <see cref="MailOAuthAccount"/> until it is revoked, so it is protected
    /// like the SMTP password and never leaves the server.
    /// </summary>
    public string? MailOAuthRefreshTokenProtected { get; set; }
    /// <summary>The mailbox that signed in, which is also who the mail comes from.</summary>
    public string? MailOAuthAccount { get; set; }
    public DateTimeOffset? MailOAuthConnectedAt { get; set; }
    /// <summary>Why the provider last refused to renew the sign-in; cleared when it next succeeds.</summary>
    public string? MailOAuthError { get; set; }

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

    // --- Restore from the admin page (dev-plan 9.4). Written by the app when
    // a restore is requested and by the sidecar when it finishes. These are
    // in the database so a restore survives the app restarting mid-way; the
    // app also holds the pending id in memory, for the minutes when the
    // database is being replaced and cannot be read at all.

    /// <summary>
    /// The restore that is pending or running. Non-null means the wiki is in
    /// maintenance: reads pass, writes are refused with 503.
    /// </summary>
    public Guid? RestoreJobId { get; set; }

    /// <summary>
    /// When the pending restore was requested. Also the moment a
    /// point-in-time restore undoes itself to, which is why the safety
    /// backup and the WAL switch happen before anything else.
    /// </summary>
    public DateTimeOffset? RestoreStartedAt { get; set; }

    /// <summary>
    /// Somebody asked to stop the pending restore. The sidecar honors this
    /// at its last check before the point of no return, and ignores it after.
    /// </summary>
    public DateTimeOffset? RestoreCancelRequestedAt { get; set; }

    /// <summary>
    /// When a restore last completed. The collab sidecar compares this with
    /// its own start time and exits when it is newer, which is how an open
    /// editor stops holding content from after the backup.
    /// </summary>
    public DateTimeOffset? LastRestoredAt { get; set; }

    /// <summary>
    /// The job that did it. At startup the app writes the audit entry and
    /// raises the alert for this job if it has not already, which is how the
    /// record lands in the restored database's own chain.
    /// </summary>
    public Guid? LastRestoreJobId { get; set; }

    /// <summary>What was restored, for the page and the audit entry: a label, or a label and a time.</summary>
    public string? LastRestoreFrom { get; set; }

    /// <summary>
    /// The copy the restore replaced, kept so the restore can be undone:
    /// the database name and the uploads directory. Null means there is
    /// none, either because it was removed or because a point-in-time
    /// restore leaves none (its undo is another point-in-time restore).
    /// </summary>
    public string? KeptCopyJson { get; set; }

    // --- Branding (dev-plan 13.1). Nothing here changes what anyone sees
    // until someone sets it on purpose: every default reproduces Tesria.

    /// <summary>
    /// The name beside the logo in the header and on the sign-in card. Null
    /// means "Tesria". Deliberately not <see cref="InstanceName"/>: the owner
    /// wants renaming the instance to leave the header alone.
    /// </summary>
    public string? BrandName { get; set; }

    /// <summary><c>logo-and-name</c> (default), <c>logo</c> or <c>name</c>. Header and sign-in both.</summary>
    public string BrandDisplay { get; set; } = "logo-and-name";

    /// <summary>
    /// How the sign-in card arranges logo and name when both are shown:
    /// <c>side-by-side</c> (default) or <c>stacked</c>. The header is always
    /// side by side.
    /// </summary>
    public string SignInArrangement { get; set; } = "side-by-side";

    /// <summary>Content hash of the uploaded logo; null means Tesria's mark.</summary>
    public string? BrandLogoHash { get; set; }
    /// <summary><c>svg</c> or <c>webp</c>.</summary>
    public string? BrandLogoFormat { get; set; }
    public int? BrandLogoWidth { get; set; }
    public int? BrandLogoHeight { get; set; }

    /// <summary>An optional logo for dark mode. Null means the light one is used in both.</summary>
    public string? BrandLogoDarkHash { get; set; }
    public string? BrandLogoDarkFormat { get; set; }
    public int? BrandLogoDarkWidth { get; set; }
    public int? BrandLogoDarkHeight { get; set; }

    /// <summary>Content hash of the uploaded favicon set; null means the generated one.</summary>
    public string? BrandFaviconHash { get; set; }
    /// <summary>Whether the favicon upload was an SVG, which is then offered to browsers first.</summary>
    public bool BrandFaviconHasSvg { get; set; }

    /// <summary><c>any</c> (people choose, the default), <c>light</c> or <c>dark</c>.</summary>
    public string ThemePolicy { get; set; } = "any";

    /// <summary><c>any</c> (people choose, the default) or <c>locked</c>.</summary>
    public string AccentPolicy { get; set; } = "any";

    /// <summary>
    /// The accent new visitors get, or everyone gets when locked: one of the
    /// six built-in names, or <c>brand</c> for the custom colors below.
    /// Null means Tesria's default (blue) with nothing set.
    /// </summary>
    public string? AccentName { get; set; }

    /// <summary>The custom accent for light mode, as normalized <c>#rrggbb</c>. Never free CSS.</summary>
    public string? BrandAccentLight { get; set; }
    /// <summary>The custom accent for dark mode, as normalized <c>#rrggbb</c>.</summary>
    public string? BrandAccentDark { get; set; }

    public DateTimeOffset? BrandChangedAt { get; set; }
    public Guid? BrandChangedById { get; set; }

    /// <summary>
    /// When the owner last reviewed the rights matrix (dev-plan 11.1). Null
    /// means nobody has looked at the defaults yet, which the Roles tab says
    /// and the setup wizard requires.
    /// </summary>
    /// <summary>
    /// The permission keys <c>RoleSeed</c> has already handed out, as a JSON
    /// array (dev-plan 11.1). It is how a right added in a later release can
    /// be given to the roles whose defaults include it, without handing back
    /// a right an owner deliberately removed.
    /// </summary>
    public string? SeededPermissionKeys { get; set; }

    public DateTimeOffset? PermissionsReviewedAt { get; set; }

    public Guid? PermissionsReviewedById { get; set; }

    /// <summary>
    /// When the owner finished first-run setup (dev-plan 10.2). Null means the
    /// wizard is still to be done; an instance upgraded from before it existed
    /// is stamped by <see cref="Infrastructure.Auth.OwnerSeed"/> instead.
    /// </summary>
    public DateTimeOffset? SetupCompletedAt { get; set; }

    /// <summary>
    /// Which wizard steps have been answered, as
    /// <c>{ "registration": { "at": "…", "skipped": false }, … }</c>
    /// (dev-plan 10.2). Kept as JSON rather than a column each because the
    /// step list is a product decision that will move, while the evidence
    /// that actually gates completion is in real columns beside this one.
    /// </summary>
    public string? SetupProgressJson { get; set; }

    // --- Versions (dev-plan 16.1).
    /// <summary>The Tesria that last started against this database.</summary>
    public string? RunningVersion { get; set; }
    /// <summary>The one before it, when it changed.</summary>
    public string? PreviousVersion { get; set; }
    public DateTimeOffset? VersionChangedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}

public enum MailSignIn
{
    Password = 0,
    Microsoft = 1,
    Google = 2,
}

public enum SmtpTlsMode
{
    None = 0,
    StartTls = 1,
    SslOnConnect = 2,
}
