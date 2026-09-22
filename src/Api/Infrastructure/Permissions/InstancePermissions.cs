using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>Where a right belongs on the Roles tab, and who it is meaningful for.</summary>
public enum PermissionScope
{
    /// <summary>Something an ordinary user might do: writing, exporting, creating spaces.</summary>
    Content = 0,

    /// <summary>Running the instance. A user-tier role holding one of these is worth an alert.</summary>
    Administration = 1,
}

/// <summary>
/// One right an instance role may hold. <paramref name="DefaultFrom"/> is the
/// lowest tier whose built-in role holds it out of the box, which is all the
/// defaults need: nothing is granted to users and withheld from administrators.
/// </summary>
public sealed record InstancePermission(
    string Key, string Area, string Label, string Description,
    PermissionScope Scope, UserRole DefaultFrom);

/// <summary>
/// The catalogue of instance rights (dev-plan 11.1).
///
/// Code, not data: which rights exist and what they are called is a decision
/// with a test beside it, while which role holds which right is configuration
/// and lives in the database. A key here is a stable string that appears in
/// <c>RolePermissions</c> rows and in route metadata, so renaming one is a
/// migration, not an edit.
///
/// Rights are additive over space permissions and never a bypass: holding
/// <c>pages.delete_any</c> says a person may delete other people's pages at
/// all, not that they may do it in a space they cannot edit.
/// </summary>
public static class InstancePermissions
{
    // --- Content
    public const string SpacesCreate = "spaces.create";
    public const string PagesDeleteOwn = "pages.delete_own";
    public const string PagesDeleteAny = "pages.delete_any";
    public const string PagesExport = "pages.export";
    public const string TokensUse = "tokens.use";
    public const string InvitesCreate = "invites.create";

    // --- People
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string UsersPromoteAdmins = "users.promote_admins";
    public const string InvitesManage = "invites.manage";
    public const string GroupsManage = "groups.manage";

    // --- Spaces
    public const string SpacesManage = "spaces.manage";
    public const string SpacesPublish = "spaces.publish";
    public const string SpacesDelete = "spaces.delete";

    // --- Security
    public const string AuditView = "audit.view";
    public const string SecurityView = "security.view";
    public const string SecurityRespond = "security.respond";
    public const string SecuritySettings = "security.settings";

    // --- Backups
    public const string BackupsView = "backups.view";
    public const string BackupsRun = "backups.run";
    public const string BackupsPolicy = "backups.policy";

    /// <summary>
    /// Replace the wiki with an older copy (dev-plan 9.4). Off for
    /// administrators by default, like promoting one: it is the most
    /// destructive thing the product can do from a web request, so an owner
    /// has to decide to allow it. Granting it is itself an alert.
    /// </summary>
    public const string BackupsRestore = "backups.restore";

    // --- Instance
    public const string DashboardView = "dashboard.view";
    public const string SettingsInstance = "settings.instance";
    public const string SettingsRegistration = "settings.registration";
    public const string SettingsEmail = "settings.email";
    public const string SettingsPublicSpaces = "settings.public_spaces";
    public const string PermissionsView = "permissions.view";
    public const string PermissionsEditUserTier = "permissions.edit_user_tier";

    // --- Reserved to the owner. Never stored as grants, never editable, and
    // always part of the owner's effective set: without them an owner could
    // configure themselves out of their own instance.
    public const string RolesAssignTier = "roles.assign_tier";
    public const string OwnershipTransfer = "ownership.transfer";
    public const string PermissionsEditAdminTier = "permissions.edit_admin_tier";

    public static readonly IReadOnlyList<InstancePermission> All =
    [
        new(SpacesCreate, "Content", "Create spaces",
            "Start a new space. The creator administers it.", PermissionScope.Content, UserRole.Member),
        new(PagesDeleteOwn, "Content", "Delete pages you created",
            "Move your own pages to the trash, where a space administrator can restore them.",
            PermissionScope.Content, UserRole.Member),
        new(PagesDeleteAny, "Content", "Delete pages created by others",
            "Trash anyone's page in a space you can edit.", PermissionScope.Content, UserRole.Admin),
        new(PagesExport, "Content", "Export pages",
            "Download a page as PDF, HTML or Markdown.", PermissionScope.Content, UserRole.Member),
        new(TokensUse, "Content", "Use API tokens",
            "Create personal API tokens, and use the API and the MCP server with them.",
            PermissionScope.Content, UserRole.Member),
        new(InvitesCreate, "Content", "Create invite links",
            "Invite someone when registration is closed.", PermissionScope.Content, UserRole.Admin),

        new(UsersView, "People", "See the user list",
            "The accounts on this instance, with their roles and status.",
            PermissionScope.Administration, UserRole.Admin),
        new(UsersManage, "People", "Manage accounts",
            "Suspend, unlock, sign out, revoke tokens and issue password resets. Never on the owner.",
            PermissionScope.Administration, UserRole.Admin),
        new(UsersAssignRoles, "People", "Assign roles",
            "Give someone a different role within their own tier.",
            PermissionScope.Administration, UserRole.Admin),
        // Off for administrators by default: promoting someone is how an
        // administrator would widen the circle that can act on the instance,
        // so an owner has to decide to allow it. Demoting an administrator
        // stays the owner's alone, so two administrators cannot unmake each
        // other.
        new(UsersPromoteAdmins, "People", "Promote users to administrator",
            "Make a user an administrator. Demoting one stays with the owner.",
            PermissionScope.Administration, UserRole.Owner),
        new(InvitesManage, "People", "Manage invite links",
            "See and revoke everyone's invite links.", PermissionScope.Administration, UserRole.Admin),
        new(GroupsManage, "People", "Manage groups",
            "Create groups and change who is in them.", PermissionScope.Administration, UserRole.Admin),

        new(SpacesManage, "Spaces", "Manage spaces",
            "See every space, archive one, and grant yourself access to administer it.",
            PermissionScope.Administration, UserRole.Admin),
        new(SpacesPublish, "Spaces", "Publish spaces",
            "Make a space readable without an account, or withdraw it.",
            PermissionScope.Administration, UserRole.Admin),
        new(SpacesDelete, "Spaces", "Delete spaces",
            "Destroy a space and every page, version, comment and attachment in it. Irreversible.",
            PermissionScope.Administration, UserRole.Admin),

        new(AuditView, "Security", "Read the audit log",
            "Every recorded action, and the chain verification.",
            PermissionScope.Administration, UserRole.Admin),
        new(SecurityView, "Security", "See security",
            "The overview, events, alerts, the blocklist and the current limits.",
            PermissionScope.Administration, UserRole.Admin),
        new(SecurityRespond, "Security", "Respond to security",
            "Acknowledge and resolve alerts; block and unblock addresses.",
            PermissionScope.Administration, UserRole.Admin),
        new(SecuritySettings, "Security", "Change security settings",
            "Rate limits, lockout, the two-factor requirement and the embed allowlist.",
            PermissionScope.Administration, UserRole.Admin),

        new(BackupsView, "Backups", "See backups",
            "Backup status, the inventory and the run log.", PermissionScope.Administration, UserRole.Admin),
        new(BackupsRun, "Backups", "Run backups",
            "Back up now, and test that a backup restores.", PermissionScope.Administration, UserRole.Admin),
        new(BackupsPolicy, "Backups", "Change the retention policy",
            "Decide how many backups are kept and for how long.",
            PermissionScope.Administration, UserRole.Admin),
        // Owner by default, like users.promote_admins: restoring replaces the
        // whole wiki with an older copy, so it is not something an
        // administrator holds until the owner says so.
        new(BackupsRestore, "Backups", "Restore a backup",
            "Replace the wiki with an older copy. The previous copy is kept so the restore can be undone.",
            PermissionScope.Administration, UserRole.Owner),

        new(DashboardView, "Instance", "See the dashboard",
            "Usage, content and health figures for the whole instance.",
            PermissionScope.Administration, UserRole.Admin),
        new(SettingsInstance, "Instance", "Change the instance name and address",
            "What this instance is called, and the address links in email use.",
            PermissionScope.Administration, UserRole.Admin),
        new(SettingsRegistration, "Instance", "Change registration",
            "Whether anyone may create an account, or only invited people.",
            PermissionScope.Administration, UserRole.Admin),
        new(SettingsEmail, "Instance", "Change the email server",
            "The SMTP settings outbound email uses.", PermissionScope.Administration, UserRole.Admin),
        new(SettingsPublicSpaces, "Instance", "Change anonymous reading",
            "The instance-wide switch for reading without an account.",
            PermissionScope.Administration, UserRole.Admin),
        new(PermissionsView, "Instance", "See roles",
            "The roles on this instance and the rights each one holds.",
            PermissionScope.Administration, UserRole.Admin),
        new(PermissionsEditUserTier, "Instance", "Edit user roles",
            "Change what user-tier roles may do, and create new ones.",
            PermissionScope.Administration, UserRole.Admin),
    ];

    /// <summary>
    /// The owner's alone, always held, never a checkbox. Editing admin-tier
    /// rights is here because an administrator who could widen their own role
    /// would make every other restriction on administrators decorative.
    /// </summary>
    public static readonly IReadOnlyList<InstancePermission> Reserved =
    [
        new(RolesAssignTier, "Always the owner", "Promote and demote administrators",
            "Move an account between the user and administrator tiers.",
            PermissionScope.Administration, UserRole.Owner),
        new(OwnershipTransfer, "Always the owner", "Transfer ownership",
            "Hand the instance to someone else. You become an administrator.",
            PermissionScope.Administration, UserRole.Owner),
        new(PermissionsEditAdminTier, "Always the owner", "Edit administrator roles",
            "Change what administrators may do, and create administrator roles.",
            PermissionScope.Administration, UserRole.Owner),
    ];

    private static readonly HashSet<string> Known = [.. All.Select(p => p.Key)];
    private static readonly HashSet<string> ReservedKeys = [.. Reserved.Select(p => p.Key)];

    /// <summary>A key the catalogue still defines. Rows for anything else are ignored.</summary>
    public static bool IsAssignable(string key) => Known.Contains(key);

    public static bool IsReserved(string key) => ReservedKeys.Contains(key);

    /// <summary>What a tier's built-in role holds before anyone edits it.</summary>
    public static IReadOnlyList<string> DefaultsFor(UserRole tier) =>
        [.. All.Where(p => tier >= p.DefaultFrom).Select(p => p.Key)];

    /// <summary>The areas in the order the Roles tab shows them.</summary>
    public static IReadOnlyList<string> Areas =>
        [.. All.Select(p => p.Area).Distinct()];
}
