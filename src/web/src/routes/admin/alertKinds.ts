/**
 * What each security alert is called, in words (dev-plan 3.3). Shared by the
 * Security tab and the notification bell, which used to show an alert by its
 * internal name ("backup.failed") because only the tab knew these.
 */
export const ALERT_KIND_LABEL: Record<string, string> = {
  'login.failed_burst_ip': 'Failed sign-in burst from one address',
  'login.credential_stuffing': 'Credential stuffing: many accounts from one address',
  'account.locked': 'Account locked',
  'account.repeated_lockouts': 'Account locked repeatedly',
  'login.admin_new_address': 'Administrator signed in from a new address',
  'http.denied_spike': 'Spike of denied requests from one address',
  'content.mass_removal': 'Many pages removed quickly',
  'token.minting_burst': 'Many API tokens minted quickly',
  'registration.burst': 'Registration burst',
  'admin.promoted': 'Administrator promoted',
  'settings.public_spaces_toggled': 'Public spaces switch changed',
  'webhook.private_target': 'Webhook aimed at a private address',
  'audit.chain_broken': 'Audit log chain broken',
  'backup.failed': 'A backup failed',
  'backup.overdue': 'Backups are overdue',
  'backup.agent_offline': 'A backup agent is not reporting',
  'backup.restore_test_failed': 'A restore test failed',
  'backup.disk_low': 'Backup disk nearly full',
  'backup.retention_reduced': 'Backup retention policy made stricter',
  'owner.transferred': 'Ownership of this instance was transferred',
  'permissions.expanded': 'A role was given more rights',
}
