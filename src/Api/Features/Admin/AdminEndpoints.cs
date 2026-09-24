using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Instance-level operations, gated by the Admin role alone: they are about
/// the instance, not about any space's content.
///
/// Note what is deliberately absent: there is no endpoint here that returns
/// page content. Admins do not bypass space permissions or page restrictions
/// (see docs/architecture.md, "Roles and administrators"); to read a space
/// they hold no grant for, an admin uses <see cref="RecoverSpaceAccess"/>,
/// which is audited and leaves a revocable grant behind. A silent bypass
/// would let any admin read any team's private space with no trace.
/// </summary>
public static class AdminEndpoints
{
    public record RecoverAccessResponse(
        Guid SpaceId, string Key, string Name, bool AlreadyHadAccess);

    /// <summary>
    /// The reset link, returned once. The administrator passes it to the user
    /// out of band (in person, over chat, however they already verify identity)
    ///which is what makes this work with no email server configured.
    /// </summary>
    public record IssuedResetResponse(string Token, string Path, DateTimeOffset ExpiresAt);

    /// <summary>
    /// <c>SendEmail</c> emails the link to <c>Email</c>, with <c>Message</c>
    /// (the inviter's own words, the default when empty) above the link.
    /// </summary>
    public record CreateInviteRequest(string? Email, int? ExpiresInDays, bool? SendEmail = null, string? Message = null);
    /// <summary>
    /// <c>Emailed</c> and <c>EmailError</c> say what happened to an email that
    /// was asked for. <c>TailnetUrl</c> is the same link on the Tailscale
    /// address, when Tesria has one, for someone who reaches it through a
    /// tailnet rather than the internet.
    /// </summary>
    public record IssuedInviteResponse(
        string Token, string Path, string? Email, DateTimeOffset ExpiresAt, bool Emailed = false, string? EmailError = null,
        string? TailnetUrl = null);
    /// <summary>What the invite form needs to offer an email: whether the server sends, and the words to start from.</summary>
    public record InviteEmailResponse(bool Enabled, string Subject, string Message);
    /// <summary><c>UsedByName</c>: the account the invite created, so the list says who, not only when.</summary>
    public record InviteResponse(
        Guid Id, string? Email, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, DateTimeOffset CreatedAt,
        string? UsedByName = null);

    public record AdminUserResponse(
        Guid Id, string Email, string DisplayName, UserRole Role, UserStatus Status,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, bool IsSso,
        int RecoveryCodesRemaining, DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt,
        int FailedLoginCount, DateTimeOffset? LockedUntil,
        /// <summary>Which role, not just which tier (dev-plan 11.2).</summary>
        Guid? RoleId, string RoleName,
        /// <summary>Whether the codes were ever confirmed saved; a count of 8 nobody saw is not recovery.</summary>
        bool RecoveryCodesSaved,
        /// <summary>Two-factor is on, so an administrator may turn it off (dev-plan 15.1).</summary>
        bool TotpEnabled);

    public record LockoutRow(Guid UserId, string Email, string DisplayName, int FailedLoginCount, DateTimeOffset LockedUntil);
    public record SecurityLimitsResponse(
        int LoginRateLimitPerMinute, int AnonymousRateLimitPerMinute, int TokenMintLimitPerHour,
        int LockoutThreshold, int LockoutBaseSeconds, int LockoutMaxSeconds,
        IReadOnlyList<LockoutRow> ActiveLockouts);

    /// <summary>
    /// Either a tier (<paramref name="Role"/>, the 10.1 promotion path) or a
    /// specific role (<paramref name="RoleId"/>, dev-plan 11.2). A role whose
    /// tier differs from the account's is a tier change and follows the same
    /// rules as one.
    /// </summary>
    public record SetRoleRequest(UserRole? Role, Guid? RoleId);
    public record SetStatusRequest(UserStatus Status);

    public record AdminSpaceResponse(
        Guid Id, string Key, string Name, string? Description, bool Archived,
        Guid CreatedById, string CreatedByName, int PageCount, long StorageBytes,
        DateTimeOffset CreatedAt, bool IsPublic, bool PublicComments, DateTimeOffset? PublicSince, int AttachmentCount);
    public record SetPublicRequest(bool IsPublic, bool? PublicComments);

    /// <summary>
    /// Note the absence of the SMTP password: it is write-only over the API.
    /// <paramref name="SmtpPasswordSet"/> tells the UI whether one exists so it
    /// can render "configured" without ever transmitting the secret.
    /// </summary>
    public record SettingsResponse(
        string InstanceName,
        string? BaseUrl,
        string EffectiveBaseUrl,
        bool AllowPublicRegistration,
        bool AllowPublicSpaces,
        bool EmailEnabled,
        string? SmtpHost,
        int SmtpPort,
        string? SmtpUsername,
        bool SmtpPasswordSet,
        string? SmtpFromAddress,
        SmtpTlsMode SmtpTls,
        bool RequireTotpForAdmins,
        string EmbedAllowlist,
        int LoginRateLimitPerMinute,
        int AnonymousRateLimitPerMinute,
        int TokenMintLimitPerHour,
        int LockoutThreshold,
        int LockoutBaseSeconds,
        int LockoutMaxSeconds,
        DateTimeOffset UpdatedAt,
        /// <summary>Which parts of this the caller may change (dev-plan 11.1).</summary>
        string[] Permissions,
        MailSignInInfo? Mail = null);

    /// <summary>
    /// The mail provider and its sign-in (dev-plan Phase 18). Like the SMTP
    /// password, the client secrets and the stored sign-in are write-only:
    /// only whether each is set. The redirect addresses are what the
    /// administrator registers with Microsoft or Google.
    /// </summary>
    public record MailSignInInfo(
        string? Provider, MailSignIn SignIn,
        string? MicrosoftClientId, bool MicrosoftClientSecretSet, string? MicrosoftTenant,
        string? GoogleClientId, bool GoogleClientSecretSet,
        string? Account, DateTimeOffset? ConnectedAt, string? Error,
        string MicrosoftRedirectUri, string GoogleRedirectUri, bool GooglePasteBack);

    /// <summary>
    /// Every field is optional: an omitted (null) field leaves the stored value
    /// alone, so a caller can change one setting without having to send, and
    /// risk clobbering, the rest.
    ///
    /// <paramref name="SmtpPassword"/> follows the same rule with one addition:
    /// an empty string means "clear it", which null cannot express.
    /// </summary>
    public record UpdateSettingsRequest(
        string? InstanceName,
        string? BaseUrl,
        bool? AllowPublicRegistration,
        bool? AllowPublicSpaces,
        bool? EmailEnabled,
        string? SmtpHost,
        int? SmtpPort,
        string? SmtpUsername,
        string? SmtpPassword,
        string? SmtpFromAddress,
        SmtpTlsMode? SmtpTls,
        bool? RequireTotpForAdmins,
        string? EmbedAllowlist,
        int? LoginRateLimitPerMinute,
        int? AnonymousRateLimitPerMinute,
        int? TokenMintLimitPerHour,
        int? LockoutThreshold,
        int? LockoutBaseSeconds,
        int? LockoutMaxSeconds,
        string? SmtpProvider = null,
        string? MicrosoftClientId = null,
        string? MicrosoftClientSecret = null,
        string? MicrosoftTenant = null,
        string? GoogleClientId = null,
        string? GoogleClientSecret = null);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        // Every route names the right it needs (dev-plan 11.1). The group
        // only requires a session; what the caller may reach inside it is the
        // matrix's business, not a blanket "is an administrator".
        var group = routes.MapGroup("/admin").WithTags("Admin").RequireAuthorization();

        // Readable by anyone who may change any part of it; the handler says which parts.
        group.MapGet("/settings", GetSettings);
        // Checked field by field inside the handler: one request may touch
        // settings from several areas.
        group.MapPut("/settings", UpdateSettings);
        group.MapPost("/settings/email/test", SendTestEmail).RequirePermission(InstancePermissions.SettingsEmail);
        group.MapPost("/spaces/{key}/recover-access", RecoverSpaceAccess).RequirePermission(InstancePermissions.SpacesManage);
        group.MapPost("/users/{userId:guid}/reset-password", IssuePasswordReset).RequirePermission(InstancePermissions.UsersManage);
        group.MapGet("/users", ListUsers).RequirePermission(InstancePermissions.UsersView);
        // Two rights reach this one route, so it is checked in the handler:
        // the owner's reserved roles.assign_tier, and users.promote_admins,
        // which an owner may grant and which only promotes (dev-plan 11.1).
        group.MapPut("/users/{userId:guid}/role", SetRole);
        group.MapPost("/users/{userId:guid}/transfer-ownership", TransferOwnership)
            .RequirePermission(InstancePermissions.OwnershipTransfer);
        group.MapPut("/users/{userId:guid}/status", SetStatus).RequirePermission(InstancePermissions.UsersManage);
        group.MapPost("/users/{userId:guid}/revoke-sessions", RevokeSessions).RequirePermission(InstancePermissions.UsersManage);
        group.MapPost("/users/{userId:guid}/revoke-tokens", RevokeTokens).RequirePermission(InstancePermissions.UsersManage);
        group.MapPost("/users/{userId:guid}/disable-two-factor", DisableTwoFactor).RequirePermission(InstancePermissions.UsersManage);
        group.MapGet("/spaces", ListSpaces).RequirePermission(InstancePermissions.SpacesManage);
        group.MapPut("/spaces/{key}/public", SetSpacePublic).RequirePermission(InstancePermissions.SpacesPublish);
        group.MapGet("/invites", ListInvites).RequirePermission(InstancePermissions.InvitesManage);
        group.MapPost("/invites", CreateInvite).RequirePermission(InstancePermissions.InvitesCreate);
        group.MapGet("/invites/email", InviteEmail).RequirePermission(InstancePermissions.InvitesCreate);
        group.MapDelete("/invites/{id:guid}", RevokeInvite).RequirePermission(InstancePermissions.InvitesManage);
        group.MapPost("/audit/verify", VerifyAuditChain).RequirePermission(InstancePermissions.AuditView);
        group.MapPost("/users/{userId:guid}/unlock", Unlock).RequirePermission(InstancePermissions.UsersManage);
        group.MapGet("/security/limits", GetLimits).RequirePermission(InstancePermissions.SecurityView);

        return routes;
    }

    /// <summary>
    /// Grants the calling admin an explicit <see cref="SpaceOperation.Admin"/>
    /// permission on a space, so they can administer (or recover) it.
    ///
    /// From that point the existing permission rules apply unchanged, including
    /// the one that already lets an explicit space admin past page restrictions,
    /// rather than adding an "unless admin" branch to every check. The grant is a
    /// normal row, so it can be revoked afterwards through the usual permissions
    /// endpoint, returning the admin to ordinary access.
    /// </summary>
    private static async Task<IResult> RecoverSpaceAccess(
        string key, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();

        // An open space (no grants at all) already lets every signed-in user
        // administer it. A grant here would be the space's first, and the
        // first grant is what makes a space private: the administrator would
        // gain nothing and lock everybody else out. So it grants nothing.
        if (!await db.SpacePermissions.AnyAsync(p => p.SpaceId == space.Id))
            return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, true));

        var userId = current.RequireId();
        var alreadyHadAccess = await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id
            && p.PrincipalType == PrincipalType.User
            && p.PrincipalId == userId
            && p.Operation == SpaceOperation.Admin);

        // Idempotent: re-running it is a no-op rather than a duplicate grant and
        // a second audit entry, so a retried request doesn't pollute the log.
        if (alreadyHadAccess)
            return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, true));

        db.SpacePermissions.Add(new SpacePermission
        {
            Id = Guid.NewGuid(),
            SpaceId = space.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = userId,
            Operation = SpaceOperation.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        audit.Record("space.access_recovered", "space", space.Id,
            new { space.Key, space.Name });
        await db.SaveChangesAsync();

        return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, false));
    }

    /// <summary>
    /// The settings a caller may see, with the rights they hold over them, so
    /// the SPA can render each section editable or read-only (dev-plan 11.1).
    /// </summary>
    private static async Task<IResult> GetSettings(
        ISiteSettingsService settings, IConfiguration config, IInstancePermissions permissions)
    {
        var held = await permissions.ForCurrentUserAsync();
        if (!SettingsFields.Keys.Any(held.Contains)) return Results.Forbid();
        return Results.Ok(ToResponse(await settings.GetAsync(), config) with
        {
            Permissions = [.. SettingsFields.Keys.Where(held.Contains)],
        });
    }

    /// <summary>
    /// Which right each settings field needs. One request may carry fields
    /// from several areas, so the check is per field rather than per route:
    /// an administrator allowed to fix the email server but not to open
    /// registration gets exactly that.
    /// </summary>
    private static class SettingsFields
    {
        public static readonly string[] Keys =
        [
            InstancePermissions.SettingsInstance, InstancePermissions.SettingsRegistration,
            InstancePermissions.SettingsEmail, InstancePermissions.SettingsPublicSpaces,
            InstancePermissions.SecuritySettings,
        ];

        public static IEnumerable<(string Field, string Key)> Required(UpdateSettingsRequest r)
        {
            if (r.InstanceName is not null) yield return (nameof(r.InstanceName), InstancePermissions.SettingsInstance);
            if (r.BaseUrl is not null) yield return (nameof(r.BaseUrl), InstancePermissions.SettingsInstance);
            if (r.AllowPublicRegistration is not null) yield return (nameof(r.AllowPublicRegistration), InstancePermissions.SettingsRegistration);
            if (r.AllowPublicSpaces is not null) yield return (nameof(r.AllowPublicSpaces), InstancePermissions.SettingsPublicSpaces);
            if (r.EmailEnabled is not null) yield return (nameof(r.EmailEnabled), InstancePermissions.SettingsEmail);
            if (r.SmtpHost is not null) yield return (nameof(r.SmtpHost), InstancePermissions.SettingsEmail);
            if (r.SmtpPort is not null) yield return (nameof(r.SmtpPort), InstancePermissions.SettingsEmail);
            if (r.SmtpUsername is not null) yield return (nameof(r.SmtpUsername), InstancePermissions.SettingsEmail);
            if (r.SmtpPassword is not null) yield return (nameof(r.SmtpPassword), InstancePermissions.SettingsEmail);
            if (r.SmtpFromAddress is not null) yield return (nameof(r.SmtpFromAddress), InstancePermissions.SettingsEmail);
            if (r.SmtpTls is not null) yield return (nameof(r.SmtpTls), InstancePermissions.SettingsEmail);
            if (r.SmtpProvider is not null) yield return (nameof(r.SmtpProvider), InstancePermissions.SettingsEmail);
            if (r.MicrosoftClientId is not null) yield return (nameof(r.MicrosoftClientId), InstancePermissions.SettingsEmail);
            if (r.MicrosoftClientSecret is not null) yield return (nameof(r.MicrosoftClientSecret), InstancePermissions.SettingsEmail);
            if (r.MicrosoftTenant is not null) yield return (nameof(r.MicrosoftTenant), InstancePermissions.SettingsEmail);
            if (r.GoogleClientId is not null) yield return (nameof(r.GoogleClientId), InstancePermissions.SettingsEmail);
            if (r.GoogleClientSecret is not null) yield return (nameof(r.GoogleClientSecret), InstancePermissions.SettingsEmail);
            if (r.RequireTotpForAdmins is not null) yield return (nameof(r.RequireTotpForAdmins), InstancePermissions.SecuritySettings);
            if (r.EmbedAllowlist is not null) yield return (nameof(r.EmbedAllowlist), InstancePermissions.SecuritySettings);
            if (r.LoginRateLimitPerMinute is not null) yield return (nameof(r.LoginRateLimitPerMinute), InstancePermissions.SecuritySettings);
            if (r.AnonymousRateLimitPerMinute is not null) yield return (nameof(r.AnonymousRateLimitPerMinute), InstancePermissions.SecuritySettings);
            if (r.TokenMintLimitPerHour is not null) yield return (nameof(r.TokenMintLimitPerHour), InstancePermissions.SecuritySettings);
            if (r.LockoutThreshold is not null) yield return (nameof(r.LockoutThreshold), InstancePermissions.SecuritySettings);
            if (r.LockoutBaseSeconds is not null) yield return (nameof(r.LockoutBaseSeconds), InstancePermissions.SecuritySettings);
            if (r.LockoutMaxSeconds is not null) yield return (nameof(r.LockoutMaxSeconds), InstancePermissions.SecuritySettings);
        }
    }

    /// <summary>
    /// Sends a short message to the calling administrator's own address, so
    /// the SMTP settings can be proven before anything depends on them. The
    /// result, including the server's error text, comes back in the body.
    /// </summary>
    private static async Task<IResult> SendTestEmail(
        Infrastructure.Email.IEmailSender email, ISiteSettingsService settings, CurrentUser current,
        AppDbContext db, IConfiguration config)
    {
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Id == current.RequireId());
        var s = await settings.GetAsync();
        var result = await email.SendAsync(new Infrastructure.Email.EmailMessage(
            me.Email,
            $"[{s.InstanceName}] Test email",
            $"This is a test message from {s.InstanceName} at {Infrastructure.Email.SiteUrl.Resolve(s, config)}.\n\n" +
            "If you are reading it, outbound email is working."));
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest req, ISiteSettingsService settings,
        CurrentUser current, IAuditLogger audit, AppDbContext db, ISecurityDetector detector,
        HttpContext http, IConfiguration config, IInstancePermissions permissions, MailOAuthService mailOAuth)
    {
        // Per field (dev-plan 11.1), and the whole request is refused rather
        // than partly applied: a half-saved settings form is worse than a
        // refusal that names what was missing.
        var held = await permissions.ForCurrentUserAsync();
        foreach (var (field, key) in SettingsFields.Required(req))
            if (!held.Contains(key))
                return Results.Json(new
                {
                    title = "Forbidden",
                    status = 403,
                    code = "permission_required",
                    permission = key,
                    // Named by the right's label, as the Roles tab shows it; the
                    // message used to give the setting's field name, such as
                    // "AllowPublicSpaces" (found 2026-09-23).
                    message = $"Your role does not have the right to {LabelOf(key)}.",
                }, statusCode: StatusCodes.Status403Forbidden);

        // The public-read switch in either direction is sudo territory
        // (dev-plan 3.5): exposing content, or undoing a mitigation.
        if (req.AllowPublicSpaces is not null && Auth.AuthEndpoints.RequireSudo(http, config) is { } denied)
            return denied;

        if (req.SmtpPort is { } port && (port < 1 || port > 65535))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["smtpPort"] = ["Port must be between 1 and 65535."],
            });

        var name = req.InstanceName?.Trim();
        if (name is { Length: 0 })
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceName"] = ["Instance name cannot be empty."],
            });

        // Recorded before the mutation so the entry names what actually changed
        // rather than the full (largely unchanged) new state, and never the
        // password itself, only that it was touched.
        var changed = new List<string>();
        if (name is not null) changed.Add(nameof(req.InstanceName));
        if (req.BaseUrl is not null)
        {
            var trimmed = req.BaseUrl.Trim();
            if (trimmed.Length > 0 && !(Uri.TryCreate(trimmed, UriKind.Absolute, out var u)
                    && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(u.Query)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["baseUrl"] = ["Enter the full address, e.g. https://wiki.example.com, or leave it blank."],
                });
            changed.Add(nameof(req.BaseUrl));
        }
        if (req.AllowPublicRegistration is not null) changed.Add(nameof(req.AllowPublicRegistration));
        if (req.AllowPublicSpaces is not null) changed.Add(nameof(req.AllowPublicSpaces));
        if (req.EmailEnabled is not null) changed.Add(nameof(req.EmailEnabled));
        if (req.SmtpHost is not null) changed.Add(nameof(req.SmtpHost));
        if (req.SmtpPort is not null) changed.Add(nameof(req.SmtpPort));
        if (req.SmtpUsername is not null) changed.Add(nameof(req.SmtpUsername));
        if (req.SmtpPassword is not null) changed.Add(nameof(req.SmtpPassword));
        if (req.SmtpFromAddress is not null) changed.Add(nameof(req.SmtpFromAddress));
        if (req.SmtpTls is not null) changed.Add(nameof(req.SmtpTls));
        if (req.SmtpProvider is not null)
        {
            if (req.SmtpProvider.Length > 0 && MailProviders.Find(req.SmtpProvider) is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["smtpProvider"] = ["That is not one of the listed providers."],
                });
            changed.Add(nameof(req.SmtpProvider));
        }
        if (req.MicrosoftClientId is not null) changed.Add(nameof(req.MicrosoftClientId));
        if (req.MicrosoftClientSecret is not null) changed.Add(nameof(req.MicrosoftClientSecret));
        if (req.MicrosoftTenant is not null) changed.Add(nameof(req.MicrosoftTenant));
        if (req.GoogleClientId is not null) changed.Add(nameof(req.GoogleClientId));
        if (req.GoogleClientSecret is not null) changed.Add(nameof(req.GoogleClientSecret));
        if (req.RequireTotpForAdmins is not null) changed.Add(nameof(req.RequireTotpForAdmins));
        if (req.EmbedAllowlist is not null) changed.Add(nameof(req.EmbedAllowlist));

        foreach (var (field, value, min, max) in new[]
        {
            (nameof(req.LoginRateLimitPerMinute), req.LoginRateLimitPerMinute, 1, 10_000),
            (nameof(req.AnonymousRateLimitPerMinute), req.AnonymousRateLimitPerMinute, 1, 100_000),
            (nameof(req.TokenMintLimitPerHour), req.TokenMintLimitPerHour, 1, 10_000),
            (nameof(req.LockoutThreshold), req.LockoutThreshold, 1, 1_000),
            (nameof(req.LockoutBaseSeconds), req.LockoutBaseSeconds, 1, 86_400),
            (nameof(req.LockoutMaxSeconds), req.LockoutMaxSeconds, 1, 86_400),
        })
        {
            if (value is null) continue;
            if (value < min || value > max)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [char.ToLowerInvariant(field[0]) + field[1..]] = [$"Must be between {min} and {max}."],
                });
            changed.Add(field);
        }

        var actorId = current.Id;
        var updated = await settings.UpdateAsync(s =>
        {
            if (name is not null) s.InstanceName = name;
            if (req.BaseUrl is not null) s.BaseUrl = Blank(req.BaseUrl)?.TrimEnd('/');
            if (req.AllowPublicRegistration is { } reg) s.AllowPublicRegistration = reg;
            if (req.AllowPublicSpaces is { } pub) s.AllowPublicSpaces = pub;
            if (req.EmailEnabled is { } mail) s.EmailEnabled = mail;
            if (req.SmtpHost is not null) s.SmtpHost = Blank(req.SmtpHost);
            if (req.SmtpPort is { } p) s.SmtpPort = p;
            if (req.SmtpUsername is not null) s.SmtpUsername = Blank(req.SmtpUsername);
            if (req.SmtpFromAddress is not null) s.SmtpFromAddress = Blank(req.SmtpFromAddress);
            if (req.SmtpTls is { } tls) s.SmtpTls = tls;
            if (req.SmtpProvider is not null) s.SmtpProvider = Blank(req.SmtpProvider);
            // A stored sign-in belongs to the app registration that made it: a
            // different client or directory cannot renew it, so it is dropped
            // and the settings say to sign in again. A new secret for the same
            // client keeps it (an expired secret is replaced this way).
            var microsoftMoved = (req.MicrosoftClientId is not null && Blank(req.MicrosoftClientId) != s.MicrosoftClientId)
                || (req.MicrosoftTenant is not null && Blank(req.MicrosoftTenant) != s.MicrosoftTenant);
            var googleMoved = req.GoogleClientId is not null && Blank(req.GoogleClientId) != s.GoogleClientId;
            if ((microsoftMoved && s.SmtpSignIn == MailSignIn.Microsoft) || (googleMoved && s.SmtpSignIn == MailSignIn.Google))
            {
                s.SmtpSignIn = MailSignIn.Password;
                s.MailOAuthRefreshTokenProtected = null;
                s.MailOAuthAccount = null;
                s.MailOAuthConnectedAt = null;
                s.MailOAuthError = null;
            }
            if (req.MicrosoftClientId is not null) s.MicrosoftClientId = Blank(req.MicrosoftClientId);
            if (req.MicrosoftTenant is not null) s.MicrosoftTenant = Blank(req.MicrosoftTenant);
            if (req.GoogleClientId is not null) s.GoogleClientId = Blank(req.GoogleClientId);
            if (req.MicrosoftClientSecret is not null)
                s.MicrosoftClientSecretProtected = Blank(req.MicrosoftClientSecret) is { } ms ? mailOAuth.Protect(ms) : null;
            if (req.GoogleClientSecret is not null)
                s.GoogleClientSecretProtected = Blank(req.GoogleClientSecret) is { } gs ? mailOAuth.Protect(gs) : null;
            if (req.RequireTotpForAdmins is { } totp) s.RequireTotpForAdmins = totp;
            // Normalized on the way in, so the stored value is exactly what
            // both the resolve endpoint and the CSP will read back.
            if (req.EmbedAllowlist is not null)
                s.EmbedAllowlist = string.Join('\n', Embeds.EmbedAllowlist.Parse(req.EmbedAllowlist));
            if (req.LoginRateLimitPerMinute is { } l1) s.LoginRateLimitPerMinute = l1;
            if (req.AnonymousRateLimitPerMinute is { } l2) s.AnonymousRateLimitPerMinute = l2;
            if (req.TokenMintLimitPerHour is { } l3) s.TokenMintLimitPerHour = l3;
            if (req.LockoutThreshold is { } l4) s.LockoutThreshold = l4;
            if (req.LockoutBaseSeconds is { } l5) s.LockoutBaseSeconds = l5;
            if (req.LockoutMaxSeconds is { } l6) s.LockoutMaxSeconds = Math.Max(l6, s.LockoutBaseSeconds);
            if (req.SmtpPassword is not null)
                s.SmtpPasswordProtected = req.SmtpPassword.Length == 0
                    ? null
                    : settings.Protect(req.SmtpPassword);
        }, actorId);

        // Audited on its own unit of work: ISiteSettingsService.UpdateAsync has
        // already saved, so the audit entry needs its own SaveChanges.
        audit.Record("settings.updated", "instance", null, new { Changed = changed });
        // Any flip of the public-read kill switch is an alert, on or off:
        // turning it on exposes content, turning it off might be the
        // attacker covering the mitigation an admin just applied.
        if (req.AllowPublicSpaces is { } toggled) await detector.PublicSpacesToggledAsync(actorId, toggled);
        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(updated, config));
    }

    /// <summary>Trims, and turns an all-whitespace value into null rather than storing blanks.</summary>
    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SettingsResponse ToResponse(SiteSettings s, IConfiguration config) => new(
        s.InstanceName,
        s.BaseUrl,
        Infrastructure.Email.SiteUrl.Resolve(s, config),
        s.AllowPublicRegistration,
        s.AllowPublicSpaces,
        s.EmailEnabled,
        s.SmtpHost,
        s.SmtpPort,
        s.SmtpUsername,
        SmtpPasswordSet: !string.IsNullOrEmpty(s.SmtpPasswordProtected),
        s.SmtpFromAddress,
        s.SmtpTls,
        s.RequireTotpForAdmins,
        s.EmbedAllowlist,
        s.LoginRateLimitPerMinute,
        s.AnonymousRateLimitPerMinute,
        s.TokenMintLimitPerHour,
        s.LockoutThreshold,
        s.LockoutBaseSeconds,
        s.LockoutMaxSeconds,
        s.UpdatedAt,
        // Filled in by GetSettings, which knows who is asking.
        Permissions: [],
        Mail: MailInfo(s, config));

    internal static MailSignInInfo MailInfo(SiteSettings s, IConfiguration config)
    {
        var (google, pasteBack) = MailOAuthService.RedirectFor(MailSignIn.Google, s, config);
        return new MailSignInInfo(
            s.SmtpProvider, s.SmtpSignIn,
            s.MicrosoftClientId, !string.IsNullOrEmpty(s.MicrosoftClientSecretProtected), s.MicrosoftTenant,
            s.GoogleClientId, !string.IsNullOrEmpty(s.GoogleClientSecretProtected),
            s.MailOAuthAccount, s.MailOAuthConnectedAt, s.MailOAuthError,
            MailOAuthService.RedirectFor(MailSignIn.Microsoft, s, config).Uri, google, pasteBack);
    }

    /// <summary>
    /// Issues a one-time, short-lived password reset for another account.
    ///
    /// The last resort when someone has lost both their password and their
    /// recovery codes, on an instance with no email. It deliberately does not
    /// set a password: an administrator should be able to restore access
    /// without ever knowing the credential that results.
    /// </summary>
    /// <summary>
    /// Refuses an administrator acting on the owner's account (dev-plan 10.1).
    ///
    /// Without this, the role guards are theatre: an administrator could issue
    /// the owner a password-reset link and walk in, or sign them out of every
    /// device on a loop. The owner acting on their own account is fine.
    /// </summary>
    /// <summary>
    /// Whose account an administrator may act on (dev-plan 10.1, 14.1). The
    /// owner's: only the owner. Another administrator's: the owner, or someone
    /// the owner has given "Manage administrators' accounts"; a reset link is
    /// an account takeover, so administrators do not reach each other by
    /// default. A user's: anyone who reached the endpoint at all.
    /// </summary>
    internal static async Task<IResult?> RefuseIfProtectedAccountAsync(User target, CurrentUser current, IInstancePermissions rights)
    {
        if (target.Id == current.Id || target.Role < UserRole.Admin) return null;
        if (await current.IsOwnerAsync()) return null;
        if (target.Role == UserRole.Owner)
            return Results.Json(new
            {
                title = "Forbidden",
                status = 403,
                message = "Only the owner can act on the owner's account.",
            }, statusCode: StatusCodes.Status403Forbidden);
        if (await rights.HasAsync(InstancePermissions.UsersManageAdmins)) return null;
        return Results.Json(new
        {
            title = "Forbidden",
            status = 403,
            code = "permission_required",
            permission = InstancePermissions.UsersManageAdmins,
            message = "Acting on another administrator's account needs the right to manage administrators' accounts, which the owner gives on the Roles tab.",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> IssuePasswordReset(
        Guid userId, AppDbContext db, CurrentUser current,
        IAuditLogger audit, IAccountRecoveryService recovery, IInstancePermissions rights)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (await RefuseIfProtectedAccountAsync(user, current, rights) is { } refused) return refused;

        if (user.PasswordHash is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["This account signs in through an identity provider and has no local password."],
            });

        var token = recovery.IssueResetToken(user.Id, current.RequireId());
        audit.Record("user.reset_issued", "user", user.Id, new { user.Email });
        await db.SaveChangesAsync();

        var expiresAt = DateTimeOffset.UtcNow.Add(AccountRecoveryService.ResetTokenLifetime);
        // A path rather than an absolute URL: the server does not reliably know
        // its own public origin (it sits behind a proxy and sees plain HTTP),
        // so the client builds the link from the address the admin is already on.
        return Results.Ok(new IssuedResetResponse(token, $"/reset?token={token}", expiresAt));
    }

    private static async Task<IResult> ListInvites(AppDbContext db)
    {
        // Ordered in memory: SQLite (the test provider) cannot ORDER BY a
        // DateTimeOffset: the same limitation AuditEndpoints works around.
        // An invite list is inherently small, so there is nothing to page.
        var rows = await db.Invites.AsNoTracking().ToListAsync();
        var userIds = rows.Where(i => i.UsedByUserId != null).Select(i => i.UsedByUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        var invites = rows.Select(i => new InviteResponse(i.Id, i.Email, i.ExpiresAt, i.UsedAt, i.CreatedAt,
            i.UsedByUserId is { } by ? names.GetValueOrDefault(by) : null));
        return Results.Ok(invites.OrderByDescending(i => i.CreatedAt).ToList());
    }

    /// <summary>
    /// Mints a single-use registration link: the only way to add someone to a
    /// closed instance without an email server.
    ///
    /// An optional address binds the invite to one person, so a forwarded link
    /// cannot be redeemed by somebody else. Leaving it blank is the "give this
    /// to whoever needs it" case, which is why it is optional rather than
    /// required.
    /// </summary>
    private static async Task<IResult> CreateInvite(
        CreateInviteRequest req, AppDbContext db, CurrentUser current,
        IAuditLogger audit, IInviteService invites, ISiteSettingsService settings,
        IEmailSender sender, IConfiguration config, CancellationToken ct)
    {
        var days = req.ExpiresInDays ?? (int)InviteService.DefaultLifetime.TotalDays;
        if (days < 1 || days > 90)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["expiresInDays"] = ["Expiry must be between 1 and 90 days."],
            });

        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
        var send = req.SendEmail == true;
        if (send && email is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Add the address to email the invite to."],
            });
        var message = req.Message?.Trim();
        if (message is { Length: > MaxInviteMessage })
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["message"] = [$"Keep the message under {MaxInviteMessage} characters."],
            });
        if (email is not null && await db.Users.AnyAsync(u => u.Email == email, ct))
            return Results.Conflict(new { message = "An account with this email already exists." });

        var token = invites.Issue(current.RequireId(), email, TimeSpan.FromDays(days));
        audit.Record("invite.created", "instance", null, new { Email = email, Days = days, Emailed = send });
        await db.SaveChangesAsync(ct);

        var expiresAt = DateTimeOffset.UtcNow.AddDays(days);
        var path = $"/register?invite={token}";
        var tailnet = TailscaleEndpoints.AddressOf(config) is { } address ? address + path : null;
        var issued = new IssuedInviteResponse(token, path, email, expiresAt, TailnetUrl: tailnet);
        if (!send) return Results.Ok(issued);

        // The token exists in plain text only now, so this is the one moment
        // it can be emailed. A failure leaves the invite in place: the page
        // still shows the link to send some other way, and says why.
        var s = await settings.GetAsync(ct);
        var (subject, defaultMessage) = await InviteEmailTextAsync(db, current, s, ct);
        var link = SiteUrl.Resolve(s, config) + path;
        // Both addresses when there are two: the person invited may reach
        // this Tesria only through the inviter's tailnet (the owner's case,
        // 2026-09-24: family in another state, nothing on the internet).
        var alsoTailnet = tailnet is not null && tailnet != link
            ? $"\n\nIf you reach {s.InstanceName} through Tailscale, use this address instead:\n{tailnet}"
            : "";
        var result = await sender.SendAsync(new EmailMessage(email!, subject,
            (string.IsNullOrEmpty(message) ? defaultMessage : message) + "\n\n" +
            $"Create your account here. The link works once, for {email}, and expires on " +
            $"{expiresAt.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)}:\n{link}" + alsoTailnet), ct);
        return Results.Ok(issued with { Emailed = result.Sent, EmailError = result.Error });
    }

    /// <summary>Long enough for a real note, short enough that nobody pastes a document into an invite.</summary>
    public const int MaxInviteMessage = 2000;

    private static async Task<IResult> InviteEmail(
        AppDbContext db, CurrentUser current, ISiteSettingsService settings, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var (subject, message) = await InviteEmailTextAsync(db, current, s, ct);
        return Results.Ok(new InviteEmailResponse(s.EmailEnabled, subject, message));
    }

    /// <summary>The subject, and the message an inviter starts from; the link is added below it when it is sent.</summary>
    private static async Task<(string Subject, string Message)> InviteEmailTextAsync(
        AppDbContext db, CurrentUser current, Domain.SiteSettings s, CancellationToken ct)
    {
        var id = current.RequireId();
        var name = await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Someone";
        return ($"[{s.InstanceName}] {name} invited you to {s.InstanceName}",
            $"Hi,\n\n{name} has invited you to join {s.InstanceName}, our wiki, where we keep our documentation and notes " +
            "and work on them together.\n\nUse the link below to create your account. It only takes a minute.");
    }

    private static async Task<IResult> RevokeInvite(
        Guid id, AppDbContext db, IAuditLogger audit)
    {
        var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id);
        if (invite is null) return Results.NotFound();

        db.Invites.Remove(invite);
        audit.Record("invite.revoked", "instance", null, new { invite.Email });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- users --------------------------------------------------------------

    private static async Task<IResult> ListUsers(AppDbContext db)
    {
        var users = await db.Users.AsNoTracking()
            .Select(u => new AdminUserResponse(
                u.Id, u.Email, u.DisplayName, u.Role, u.Status,
                u.AvatarKey == null ? null : u.AvatarHash, u.AvatarVariant,
                u.PasswordHash != null, u.OidcSubject != null,
                db.RecoveryCodes.Count(c => c.UserId == u.Id && c.UsedAt == null),
                u.LastSeenAt, u.CreatedAt, u.FailedLoginCount, u.LockedUntil,
                u.RoleId, u.InstanceRole == null ? "" : u.InstanceRole.Name,
                u.RecoveryCodesAcknowledgedAt != null, u.TotpEnabledAt != null))
            .ToListAsync();

        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset, and this
        // list is bounded by the instance's user count. The owner leads, then
        // administrators: the rows whose role someone came here to check.
        return Results.Ok(users
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.DisplayName)
            .ToList());
    }

    /// <summary>
    /// Promotes or demotes an administrator. The owner's own role is not
    /// changeable here: ownership moves by transfer, which keeps the seat
    /// filled, so the instance can never be left with nobody able to
    /// administer it (dev-plan 10.1).
    /// </summary>
    private static async Task<IResult> SetRole(
        Guid userId, SetRoleRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, HttpContext http, IConfiguration config, IInstancePermissions rights)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();

        // A role names the tier it belongs to, so both shapes of request end
        // up as "which role, and therefore which tier" (dev-plan 11.2).
        Role? target = null;
        if (req.RoleId is { } roleId)
        {
            target = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
            if (target is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roleId"] = ["That role no longer exists."],
                });
        }
        var tier = target?.Tier ?? req.Role
            ?? throw new BadHttpRequestException("Either role or roleId is required.");

        if (user.Role == tier && (target is null || user.RoleId == target.Id))
            return Results.Ok(await OneUserAsync(db, userId));

        // The owner may make any change; an administrator holding
        // users.promote_admins may only promote a user, never demote an
        // administrator, so administrators cannot unmake one another. Moving
        // someone between roles inside their own tier is its own right.
        var held = await rights.ForCurrentUserAsync();
        var promoting = user.Role == UserRole.Member && tier == UserRole.Admin;
        var sameTier = user.Role == tier;
        if (sameTier)
        {
            var mayAssign = held.Contains(InstancePermissions.UsersAssignRoles)
                && (tier < UserRole.Admin || held.Contains(InstancePermissions.PermissionsEditAdminTier));
            if (!mayAssign)
                return Results.Json(new
                {
                    title = "Forbidden",
                    status = 403,
                    code = "permission_required",
                    permission = tier >= UserRole.Admin
                        ? InstancePermissions.PermissionsEditAdminTier
                        : InstancePermissions.UsersAssignRoles,
                    message = tier >= UserRole.Admin
                        ? "Only the owner moves an administrator between roles."
                        : "Your role does not allow assigning roles.",
                }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (!sameTier
            && !held.Contains(InstancePermissions.RolesAssignTier)
            && !(promoting && held.Contains(InstancePermissions.UsersPromoteAdmins)))
            return Results.Json(new
            {
                title = "Forbidden",
                status = 403,
                code = "permission_required",
                permission = promoting ? InstancePermissions.UsersPromoteAdmins : InstancePermissions.RolesAssignTier,
                message = promoting
                    ? "Your role does not allow promoting people to administrator."
                    : "Only the owner changes an administrator's role.",
            }, statusCode: StatusCodes.Status403Forbidden);
        // Changing who administers the instance is sudo territory (dev-plan 3.5).
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        if (user.Role == UserRole.Owner || tier == UserRole.Owner)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = ["Ownership is transferred, not assigned."],
            });

        var from = await db.Roles.AsNoTracking().Where(r => r.Id == user.RoleId)
            .Select(r => r.Name).FirstOrDefaultAsync();
        user.Role = tier;
        // A named role when one was asked for, the tier's built-in otherwise.
        user.RoleId = target?.Id
            ?? await Infrastructure.Permissions.RoleSeed.BuiltInIdAsync(db, tier)
            ?? user.RoleId;
        var to = target?.Name ?? Role.NameFor(tier);
        audit.Record("user.role_changed", "user", user.Id,
            new { user.Email, Role = tier.ToString(), From = from, To = to });
        // A new administrator is the single most valuable thing an attacker
        // with one admin session can create; every one is an alert.
        if (tier == UserRole.Admin && !sameTier) await detector.AdminPromotedAsync(current.RequireId(), user);
        await db.SaveChangesAsync();
        return Results.Ok(await OneUserAsync(db, userId));
    }

    /// <summary>
    /// Hands the instance to someone else (dev-plan 10.1): the target becomes
    /// the owner and the caller becomes an administrator, in one save, so
    /// there is never a moment with two owners or none.
    ///
    /// Nothing is rotated. The role is read from the row on every request, so
    /// both people's existing sessions simply mean something different from
    /// the next request onwards.
    /// </summary>
    private static async Task<IResult> TransferOwnership(
        Guid userId, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (target is null) return Results.NotFound();

        var meId = current.RequireId();
        if (target.Id == meId)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["You already own this instance."],
            });
        if (target.Status != UserStatus.Active)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["A suspended account cannot own the instance."],
            });

        // Giving the instance away is the most destructive administrative
        // action there is (dev-plan 3.5).
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        var me = await db.Users.FirstAsync(u => u.Id == meId);
        target.Role = UserRole.Owner;
        target.RoleId = await Infrastructure.Permissions.RoleSeed.BuiltInIdAsync(db, UserRole.Owner) ?? target.RoleId;
        me.Role = UserRole.Admin;
        me.RoleId = await Infrastructure.Permissions.RoleSeed.BuiltInIdAsync(db, UserRole.Admin) ?? me.RoleId;

        audit.Record("owner.transferred", "user", target.Id,
            new { From = me.Email, To = target.Email });
        // Every administrator hears, including the one who just gave it away:
        // if this was not their doing, it is the last moment they could act.
        await detector.OwnerTransferredAsync(meId, new { From = me.Email, To = target.Email });
        await db.SaveChangesAsync();

        return Results.Ok(await OneUserAsync(db, target.Id));
    }

    /// <summary>
    /// Suspends or reactivates an account. Suspension rotates the security
    /// stamp, so existing sessions stop working on their next request rather
    /// than lingering until the cookie expires: the thing that makes a
    /// suspension actually mean something.
    /// </summary>
    private static async Task<IResult> SetStatus(
        Guid userId, SetStatusRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IInstancePermissions rights)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (req.Status != UserStatus.Active && user.Role == UserRole.Admin
            && await RefuseIfProtectedAccountAsync(user, current, rights) is { } refused)
            return refused;

        // Suspending yourself locks you out with no way back in.
        if (userId == current.RequireId() && req.Status != UserStatus.Active)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["You cannot suspend your own account."],
            });

        // The owner is the instance's way back in; suspending it would leave
        // nobody who can transfer ownership or change a role.
        if (req.Status != UserStatus.Active && user.Role == UserRole.Owner)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["The owner cannot be suspended. Transfer ownership first."],
            });

        if (req.Status != UserStatus.Active && user.Role >= UserRole.Admin)
        {
            var otherAdmins = await db.Users.CountAsync(u =>
                u.Role >= UserRole.Admin && u.Id != userId && u.Status == UserStatus.Active);
            if (otherAdmins == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["This is the only active administrator."],
                });
        }

        user.Status = req.Status;
        if (req.Status != UserStatus.Active) user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("user.status_changed", "user", user.Id,
            new { user.Email, Status = req.Status.ToString() });
        await db.SaveChangesAsync();
        return Results.Ok(await OneUserAsync(db, userId));
    }

    /// <summary>Signs every device out of an account without changing its password.</summary>
    private static async Task<IResult> RevokeSessions(
        Guid userId, AppDbContext db, IAuditLogger audit, CurrentUser current, IInstancePermissions rights)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (await RefuseIfProtectedAccountAsync(user, current, rights) is { } refused) return refused;

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        // The stamp rotation is what kills the cookies; the rows are marked
        // so the sessions list tells the truth about what happened.
        var now = DateTimeOffset.UtcNow;
        foreach (var s in await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync())
            s.RevokedAt = now;
        audit.Record("user.sessions_revoked", "user", user.Id, new { user.Email });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Revokes every API token an account holds. Separate from sessions on
    /// purpose: tokens authenticate through a different scheme and a session
    /// revocation does not touch them, so "lock this account out" needs both.
    /// </summary>
    private static async Task<IResult> RevokeTokens(
        Guid userId, AppDbContext db, IAuditLogger audit, CurrentUser current, IInstancePermissions rights)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (await RefuseIfProtectedAccountAsync(user, current, rights) is { } refused) return refused;

        var tokens = await db.ApiTokens.Where(t => t.UserId == userId).ToListAsync();
        db.ApiTokens.RemoveRange(tokens);
        audit.Record("user.tokens_revoked", "user", userId, new { user.Email, Count = tokens.Count });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Turns off another account's two-factor (dev-plan 15.1): the way back for
    /// someone who has lost the phone and every recovery code, since a password
    /// reset never touches two-factor. Never the owner, whom nobody else may
    /// act on; another administrator only by the owner, as with promotion.
    /// Signs the account out everywhere, asks for the caller's password, and
    /// alerts every administrator.
    /// </summary>
    private static async Task<IResult> DisableTwoFactor(
        Guid userId, AppDbContext db, IAuditLogger audit, CurrentUser current, ITotpService totp,
        ISecurityDetector detector, HttpContext http, IConfiguration config, IInstancePermissions rights)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        var callerId = current.RequireId();
        if (user.Role == UserRole.Owner)
            return Results.Json(new { title = "Forbidden", status = 403,
                message = "The owner turns two-factor off from their own profile." }, statusCode: StatusCodes.Status403Forbidden);
        if (userId == callerId)
            return Results.Conflict(new { message = "Turn your own two-factor off from your profile." });
        if (await RefuseIfProtectedAccountAsync(user, current, rights) is { } refused) return refused;
        if (user.TotpEnabledAt is null)
            return Results.Conflict(new { message = "Two-factor is not on for this account." });

        totp.Disable(user);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        foreach (var s in await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync())
            s.RevokedAt = now;
        audit.Record("user.totp_disabled", "user", user.Id, new { user.Email });
        await detector.TwoFactorRemovedAsync(callerId, user);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<AdminUserResponse?> OneUserAsync(AppDbContext db, Guid userId) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AdminUserResponse(
                u.Id, u.Email, u.DisplayName, u.Role, u.Status,
                u.AvatarKey == null ? null : u.AvatarHash, u.AvatarVariant,
                u.PasswordHash != null, u.OidcSubject != null,
                db.RecoveryCodes.Count(c => c.UserId == u.Id && c.UsedAt == null),
                u.LastSeenAt, u.CreatedAt, u.FailedLoginCount, u.LockedUntil,
                u.RoleId, u.InstanceRole == null ? "" : u.InstanceRole.Name,
                u.RecoveryCodesAcknowledgedAt != null, u.TotpEnabledAt != null))
            .FirstOrDefaultAsync();

    // ---- spaces -------------------------------------------------------------

    /// <summary>
    /// Every space on the instance: metadata only, never content. Admins do
    /// not bypass space permissions (see the roles spec), so this deliberately
    /// returns counts and ownership rather than anything readable.
    /// </summary>
    private static async Task<IResult> ListSpaces(AppDbContext db) =>
        Results.Ok((await SpaceRowsAsync(db, null)).OrderBy(s => s.Key).ToList());

    private static async Task<List<AdminSpaceResponse>> SpaceRowsAsync(AppDbContext db, Guid? onlyId)
    {
        var query = db.Spaces.AsNoTracking();
        if (onlyId is { } id) query = query.Where(s => s.Id == id);
        return await query
            .Select(s => new AdminSpaceResponse(
                s.Id, s.Key, s.Name, s.Description, s.Archived,
                s.CreatedById,
                s.CreatedBy == null ? "Deleted user" : s.CreatedBy.DisplayName,
                db.Pages.Count(p => p.SpaceId == s.Id),
                db.Attachments
                    .Where(a => db.Pages.Any(p => p.Id == a.PageId && p.SpaceId == s.Id))
                    .Sum(a => (long?)a.Size) ?? 0L,
                s.CreatedAt, s.IsPublic, s.PublicComments, s.PublicSince,
                db.Attachments.Count(a => db.Pages.Any(p => p.Id == a.PageId && p.SpaceId == s.Id))))
            .ToListAsync();
    }

    /// <summary>
    /// Walks the audit log's hash chain (dev-plan 3.1) and reports the first
    /// link that does not hold. Audited itself, so the act of checking is on
    /// the record; the check reads only, and the runtime role could not alter
    /// the chain even if the code tried.
    /// </summary>
    private static async Task<IResult> VerifyAuditChain(
        IAuditChainVerifier verifier, IAuditLogger audit, AppDbContext db)
    {
        var report = await verifier.VerifyAsync();
        audit.Record("audit.chain_verified", "instance", null,
            new { report.Ok, report.Checked, report.BrokenAtSequence });
        await db.SaveChangesAsync();
        return Results.Ok(report);
    }

    /// <summary>Clears a lockout early. Audited: unlocking is an act, not a state.</summary>
    private static async Task<IResult> Unlock(Guid userId, AppDbContext db, IAuditLogger audit)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();

        AuthLockout.Reset(user);
        audit.Record("user.unlocked", "user", user.Id);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>The current limits and who is locked out right now: the Security page's counters.</summary>
    private static async Task<IResult> GetLimits(ISiteSettingsService settings, AppDbContext db)
    {
        var s = await settings.GetAsync();
        var now = DateTimeOffset.UtcNow;
        // Compared in memory: the SQLite test provider cannot translate a
        // DateTimeOffset comparison, and lockouts are few.
        var locked = (await db.Users.AsNoTracking()
                .Where(u => u.LockedUntil != null)
                .Select(u => new { u.Id, u.Email, u.DisplayName, u.FailedLoginCount, u.LockedUntil })
                .ToListAsync())
            .Where(u => u.LockedUntil > now)
            .OrderByDescending(u => u.LockedUntil)
            .Select(u => new LockoutRow(u.Id, u.Email, u.DisplayName, u.FailedLoginCount, u.LockedUntil!.Value))
            .ToList();

        return Results.Ok(new SecurityLimitsResponse(
            s.LoginRateLimitPerMinute, s.AnonymousRateLimitPerMinute, s.TokenMintLimitPerHour,
            s.LockoutThreshold, s.LockoutBaseSeconds, s.LockoutMaxSeconds, locked));
    }

    /// <summary>
    /// Publishes a space to the world, or withdraws it (dev-plan 5.1). A site
    /// administrator's act, in sudo mode: exposing content is an instance-level
    /// risk. Publishing needs the instance switch on; withdrawing never does.
    /// Audited, and always a security alert in both directions.
    /// </summary>
    private static async Task<IResult> SetSpacePublic(
        string key, SetPublicRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISiteSettingsService settings, ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        if (req.IsPublic && !(await settings.GetAsync()).AllowPublicSpaces)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["isPublic"] = ["Turn on \"Allow public spaces\" in Settings first, and read the internet-readiness checklist it points to."],
            });

        var changed = space.IsPublic != req.IsPublic;
        space.IsPublic = req.IsPublic;
        space.PublicSince = req.IsPublic ? (space.PublicSince ?? DateTimeOffset.UtcNow) : null;
        if (req.PublicComments is { } pc) space.PublicComments = pc;

        if (changed)
        {
            audit.Record(req.IsPublic ? "space.published" : "space.unpublished", "space", space.Id, new { space.Key, space.Name });
            await detector.SpaceVisibilityChangedAsync(current.RequireId(), space, req.IsPublic);
        }
        else
        {
            audit.Record("space.public_settings_changed", "space", space.Id, new { space.Key, space.PublicComments });
        }
        await db.SaveChangesAsync();
        return Results.Ok(await OneSpaceAsync(db, space.Id));
    }

    private static async Task<AdminSpaceResponse> OneSpaceAsync(AppDbContext db, Guid spaceId) =>
        (await SpaceRowsAsync(db, spaceId)).Single();

    /// <summary>A right's label as a phrase: "Change anonymous reading" becomes "change anonymous reading".</summary>
    private static string LabelOf(string key)
    {
        var label = InstancePermissions.All.FirstOrDefault(p => p.Key == key)?.Label ?? key;
        return label.Length > 0 ? char.ToLowerInvariant(label[0]) + label[1..] : label;
    }
}
