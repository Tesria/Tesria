namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// What each security alert is called, in words, for alert emails. The SPA
/// has the same list (src/web/src/routes/admin/alertKinds.ts); keep the two in
/// step. Emails used to name an alert by its key, such as "space.deleted"
/// (found 2026-09-23).
/// </summary>
public static class AlertKinds
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["login.failed_burst_ip"] = "Failed sign-in burst from one address",
        ["login.credential_stuffing"] = "Credential stuffing: many accounts from one address",
        ["account.locked"] = "Account locked",
        ["account.repeated_lockouts"] = "Account locked repeatedly",
        ["login.admin_new_address"] = "Administrator signed in from a new address",
        ["http.denied_spike"] = "Spike of denied requests from one address",
        ["content.mass_removal"] = "Many pages removed quickly",
        ["token.minting_burst"] = "Many API tokens minted quickly",
        ["registration.burst"] = "Registration burst",
        ["admin.promoted"] = "Administrator promoted",
        ["settings.public_spaces_toggled"] = "Public spaces switch changed",
        ["webhook.private_target"] = "Webhook aimed at a private address",
        ["audit.chain_broken"] = "Audit log chain broken",
        ["backup.failed"] = "A backup failed",
        ["backup.overdue"] = "Backups are overdue",
        ["backup.agent_offline"] = "A backup agent is not reporting",
        ["backup.restore_test_failed"] = "A restore test failed",
        ["backup.disk_low"] = "Backup disk nearly full",
        ["backup.retention_reduced"] = "Backup retention policy made stricter",
        ["owner.transferred"] = "Ownership of this instance was transferred",
        ["permissions.expanded"] = "A role was given more rights",
        ["user.totp_disabled"] = "Two-factor turned off for an account",
        ["space.opened"] = "A private space was made open to everyone",
        ["space.published"] = "A space was published to the internet",
        ["space.unpublished"] = "A space was withdrawn from public reading",
        ["space.deleted"] = "A space was deleted",
        ["backup.restored"] = "The wiki was restored from a backup",
        ["backup.offsite_absent"] = "No offsite backup is configured",
        ["backup.offsite_manual_only"] = "The only offsite backup is a removable drive",
        ["backup.offsite_failed"] = "An offsite backup failed",
        ["backup.offsite_stale"] = "An offsite backup is out of date",
        ["backup.offsite_archive_gap"] = "Changes are not reaching the cloud backup",
        ["backup.offsite_drill_failed"] = "An offsite restore drill failed",
    };

    /// <summary>The words for <paramref name="kind"/>, or the key itself for one nobody has named.</summary>
    public static string Label(string kind) => Labels.TryGetValue(kind, out var label) ? label : kind;
}
