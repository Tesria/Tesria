import { requestReauth } from '../auth/reauth'
import type { SpaceIconKind } from '../components/spaceIconIdentity'
import type { SpaceTreeStyle } from '../components/treeMarkers'

// Typed client for the Tesria REST API. All calls are same-origin and
// send the auth cookie automatically (credentials: 'include' for dev CORS).

/** Matches Api.Domain.UserRole. A const object rather than a TS `enum`:
 *  this project builds with `erasableSyntaxOnly`, which rejects enums. */
/** Owner is above Admin, and every check is "this or above" (dev-plan 10.1). */
export const UserRole = { Member: 0, Admin: 1, Owner: 2 } as const
export type UserRole = (typeof UserRole)[keyof typeof UserRole]

export type User = {
  id: string
  email: string
  displayName: string
  role: UserRole
  /** Content hash of the uploaded avatar, or null for the generated default.
   *  Used as the cache-busting `?v=` on the avatar URL. */
  avatarHash: string | null
  /** False for OIDC-provisioned accounts: their identity provider owns the
   *  email address and password, so the profile page renders those read-only. */
  hasPassword: boolean
  /** Chosen generated avatar, or null to derive one from the id. Ignored when
   *  `avatarHash` is set: an uploaded image always wins. */
  avatarVariant: number | null
  /** Unused recovery codes. Zero means this account has no way back in if the
   *  password is lost, which is what the post-login prompt exists to fix. */
  recoveryCodesRemaining: number
  /** Whether the codes were ever confirmed saved. Registration creates them,
   *  so a count alone cannot tell codes someone has from codes never shown. */
  recoveryCodesSaved: boolean
  /** Two-factor sign-in (dev-plan 3.5). */
  totpEnabled: boolean
  /** An administrator who must enroll before administering. */
  totpRequired: boolean
  /** Two-factor is compulsory for this account, so it cannot be turned off. */
  totpMandatory: boolean
  /** 0 off, 1 immediate, 2 daily digest (dev-plan 4.3). */
  emailNotifications: EmailNotificationMode
  /** Instance rights this account holds (dev-plan 11.1). The UI renders from
   *  these; every one is enforced server-side as well. */
  permissions: string[]
  /** The role's name: User, Administrator, Owner, or a custom one. */
  roleName: string
  /** The owner of an instance whose first-run setup is unfinished (dev-plan 10.2). */
  setupRequired: boolean
  /** The tour and tips this person has or has not seen (dev-plan 10.3). */
  onboarding: OnboardingSummary
}

/** What the SPA knows on load about the tour and tips (dev-plan 10.3). */
export type OnboardingSummary = {
  tourDue: boolean
  tipsEnabled: boolean
  dismissedTips: string[]
}

/** Every field optional: one call changes one thing. */
export type OnboardingUpdate = {
  tourCompleted?: boolean
  tourSkipped?: boolean
  tipsEnabled?: boolean
  dismissTip?: string
  resetTips?: boolean
  resetTour?: boolean
}

/** Instance rights (dev-plan 11.1). Keys match Infrastructure/Permissions/InstancePermissions.cs. */
export const Permission = {
  SpacesCreate: 'spaces.create',
  PagesDeleteOwn: 'pages.delete_own',
  PagesDeleteAny: 'pages.delete_any',
  PagesExport: 'pages.export',
  TokensUse: 'tokens.use',
  InvitesCreate: 'invites.create',
  UsersView: 'users.view',
  UsersManage: 'users.manage',
  UsersAssignRoles: 'users.assign_roles',
  UsersPromoteAdmins: 'users.promote_admins',
  InvitesManage: 'invites.manage',
  GroupsManage: 'groups.manage',
  SpacesManage: 'spaces.manage',
  SpacesPublish: 'spaces.publish',
  SpacesDelete: 'spaces.delete',
  SpacesExports: 'spaces.exports',
  AuditView: 'audit.view',
  SecurityView: 'security.view',
  SecurityRespond: 'security.respond',
  SecuritySettings: 'security.settings',
  BackupsView: 'backups.view',
  BackupsRun: 'backups.run',
  BackupsPolicy: 'backups.policy',
  BackupsRestore: 'backups.restore',
  DashboardView: 'dashboard.view',
  SettingsInstance: 'settings.instance',
  SettingsRegistration: 'settings.registration',
  SettingsEmail: 'settings.email',
  SettingsPublicSpaces: 'settings.public_spaces',
  SettingsBranding: 'settings.branding',
  PermissionsView: 'permissions.view',
  PermissionsEditUserTier: 'permissions.edit_user_tier',
  RolesAssignTier: 'roles.assign_tier',
  OwnershipTransfer: 'ownership.transfer',
  PermissionsEditAdminTier: 'permissions.edit_admin_tier',
} as const

export type InstancePermissionDto = {
  key: string
  area: string
  label: string
  description: string
  scope: 'Content' | 'Administration'
}

export type InstanceRole = {
  id: string
  /** `user`, `admin`, `owner` for the built-ins; null for a custom role. */
  key: string | null
  name: string
  description: string | null
  tier: UserRole
  builtIn: boolean
  permissions: string[]
  members: number
  /** Whether this viewer may change this row. */
  editable: boolean
}

export type PermissionMatrix = {
  catalog: InstancePermissionDto[]
  /** The owner's three, shown without checkboxes. */
  reserved: InstancePermissionDto[]
  roles: InstanceRole[]
  reviewedAt: string | null
  reviewedByName: string | null
}

export const EmailNotificationMode = { Off: 0, Immediate: 1, DailyDigest: 2 } as const
export type EmailNotificationMode = (typeof EmailNotificationMode)[keyof typeof EmailNotificationMode]

/** The password was right; the sign-in is not finished until a code is given. */
export type TotpChallenge = { requiresTotp: true; challenge: string }
export type TotpSetup = { secret: string; otpauthUri: string }
export type Session = {
  id: string
  createdAt: string
  lastSeenAt: string
  ip: string | null
  userAgent: string | null
  current: boolean
  revokedAt: string | null
}

/** The URL for a user's uploaded avatar. The hash makes each version its own
 *  URL, so the response can be cached indefinitely and never go stale. */
export const avatarUrl = (userId: string, hash: string) =>
  `/api/media/avatars/${userId}?v=${hash}`

/**
 * What came back from importing a pack (dev-plan 8.5). The restriction counts
 * and the author names are the two things the import deliberately did not
 * carry, reported so the person who ran it knows to go and set them.
 */
export type ImportedPack = {
  key: string
  name: string
  pages: number
  versions: number
  attachments: number
  comments: number
  templates: number
  labels: number
  source: string | null
  spaceRestrictions: number
  pageRestrictions: number
  authors: string[]
}

export type Space = {
  id: string
  key: string
  name: string
  description: string | null
  archived: boolean
  homepageId: string | null
  createdAt: string
  /** Readable without an account while the instance allows public spaces (dev-plan 5.1). */
  isPublic: boolean
  publicComments: boolean
  /** Icon (dev-plan 6): 0 generated, 1 emoji, 2 uploaded picture. */
  iconKind: SpaceIconKind
  /** The emoji, or an uploaded picture's content hash; null when generated. */
  iconValue: string | null
  /** Tile color index, or null to derive one from the key. */
  iconColor: number | null
  /** Which exports this space allows (dev-plan 12.3). All on unless an administrator turned some off. */
  exports: SpaceExports
  /** How the page tree marks its pages: 0 plain, 1 numbered, 2 bulleted (dev-plan 15.8). */
  treeStyle?: SpaceTreeStyle
}

export type SpaceExports = { markdown: boolean; html: boolean; pdf: boolean; site: boolean; pack: boolean }

/** First-run setup (dev-plan 10.2). */
export type SetupStep = { at: string; skipped: boolean }
export type SetupStatus = {
  required: boolean
  completedAt: string | null
  steps: Record<string, SetupStep>
}

/** What the SPA may know before a session exists (dev-plan 5.5). */
export type InstanceInfo = {
  instanceName: string
  /** No accounts yet, so the first one to register becomes the owner. */
  needsOwner: boolean
  /** Anonymous reading is opt-in twice: the instance switch AND a public space. */
  publicReading: boolean
  allowPublicRegistration: boolean
  /** The instance's branding (dev-plan 13.1). Every default is Tesria's. */
  branding: Branding
  /** The server makes its own certificate, so each device trusts it at /trust (15.5). */
  ownCertificate?: boolean
}

export type BrandDisplay = 'logo-and-name' | 'logo' | 'name'
export type SignInArrangement = 'side-by-side' | 'stacked'
export type ThemePolicy = 'any' | 'light' | 'dark'
export type AccentPolicy = 'any' | 'locked'

/** One logo: where it is, and its shape, so the page can lay it out before it loads. */
export type BrandLogo = { url: string; format: 'svg' | 'webp'; width: number | null; height: number | null }

export type Branding = {
  /** The brand name, or "Tesria". */
  name: string
  hasCustomName: boolean
  /** A brand name or a logo: what "Powered by Tesria" keys off. */
  hasIdentity: boolean
  display: BrandDisplay
  signInArrangement: SignInArrangement
  logo: BrandLogo | null
  logoDark: BrandLogo | null
  hasFavicon: boolean
  themePolicy: ThemePolicy
  accentPolicy: AccentPolicy
  accentName: string | null
  accentLight: string | null
  accentDark: string | null
}

/** Tesria, unbranded: what the page shows before /api/instance answers, and if it never does. */
export const DEFAULT_BRANDING: Branding = {
  name: 'Tesria',
  hasCustomName: false,
  hasIdentity: false,
  display: 'logo-and-name',
  signInArrangement: 'side-by-side',
  logo: null,
  logoDark: null,
  hasFavicon: false,
  themePolicy: 'any',
  accentPolicy: 'any',
  accentName: null,
  accentLight: null,
  accentDark: null,
}

/** How readable an accent is in one mode (dev-plan 13.1, decision 5). */
export type AccentCheck = {
  mode: 'light' | 'dark'
  color: string
  tokens: { primary: string; primaryDark: string; primarySoft: string; primarySofter: string; primarySoftBorder: string; onPrimary: string }
  primaryVsBackground: number
  onPrimaryVsPrimary: number
  passes: boolean
  suggested: string | null
}

/** Administration → Branding. */
export type BrandingSettings = {
  brandName: string | null
  display: BrandDisplay
  signInArrangement: SignInArrangement
  themePolicy: ThemePolicy
  accentPolicy: AccentPolicy
  accentName: string | null
  accentLight: string | null
  accentDark: string | null
  logo: BrandLogo | null
  logoDark: BrandLogo | null
  faviconHash: string | null
  faviconHasSvg: boolean
  isCustomized: boolean
  changedAt: string | null
  changedByName: string | null
  checks: AccentCheck[]
}

export type BrandingInput = {
  brandName: string | null
  display: BrandDisplay
  signInArrangement: SignInArrangement
  themePolicy: ThemePolicy
  accentPolicy: AccentPolicy
  accentName: string | null
  accentLight: string | null
  accentDark: string | null
}

/** What the delete dialog counts up before asking (dev-plan 11.3). */
export type SpaceDeletionPreview = {
  key: string
  name: string
  /** Every page, including drafts and whatever is already in the trash. */
  pages: number
  attachments: number
  bytes: number
  isPublic: boolean
}

export type PageDetail = {
  id: string
  spaceId: string
  parentPageId: string | null
  title: string
  position: number
  status: number
  currentVersionNumber: number
  contentJson: string
  fullWidth: boolean
  /** Who wrote it, for "delete pages you created" (dev-plan 11.1). */
  createdById: string
  createdAt: string
  updatedAt: string
  /** Whether you may edit it; sent with a single-page read only. */
  canEdit?: boolean | null
  /** Shown before the title (dev-plan 15.7). */
  emoji?: string | null
}

export type PageTreeNode = {
  id: string
  title: string
  position: number
  children: PageTreeNode[]
  /** Shown before the title (dev-plan 15.7). */
  emoji?: string | null
}

export type TrashedPage = {
  id: string
  title: string
  deletedAt: string
  deletedById: string | null
}

export type VersionMeta = {
  id: string
  versionNumber: number
  changeComment: string | null
  authorId: string
  authorName: string
  authorAvatarHash: string | null
  authorAvatarVariant: number | null
  createdAt: string
}

export type VersionContent = VersionMeta & { contentJson: string }

export type CollabToken = { token: string; documentName: string; enabled: boolean }

export type PageTemplate = {
  id: string
  spaceId: string | null
  name: string
  description: string | null
  contentJson: string
  createdById: string
  createdAt: string
  createdByName: string | null
  /** Whether the caller may rename or delete it. */
  canManage: boolean
}

/** The authenticated download URL for an attachment, for src/href attributes. */
export function attachmentDownloadUrl(id: string): string {
  return `/api/attachments/${id}/download`
}

export type EmbedResolution = {
  allowed: boolean
  url: string | null
  provider: string | null
  aspectRatio: string | null
  reason: string | null
}

export type LinkPreview = {
  url: string
  title: string | null
  description: string | null
  siteName: string | null
  imageUrl: string | null
  error: string | null
}

export type Attachment = {
  id: string
  pageId: string
  filename: string
  contentType: string
  size: number
  uploadedById: string
  createdAt: string
}

/** Matches the API enums: PrincipalType, SpaceOperation, PageOperation. */
export const PrincipalType = { User: 0, Group: 1 } as const
export const SpaceOperation = { View: 0, Edit: 1, Admin: 2 } as const
export const PageOperation = { View: 0, Edit: 1 } as const

export const spaceOperationName = ['View', 'Edit', 'Admin']
export const pageOperationName = ['View', 'Edit']

/** builtIn: Owner, Admins or Users, whose members follow each account's role (dev-plan 15.1). */
export type Group = { id: string; name: string; description: string | null; memberCount: number; builtIn?: boolean }
export type GroupMember = { userId: string; email: string; displayName: string }
export const UserStatus = { Active: 0, Suspended: 1 } as const
export type UserStatus = (typeof UserStatus)[keyof typeof UserStatus]

export type SiteSettings = {
  instanceName: string
  /** Override for links in email; null means the deploy-time value. */
  baseUrl: string | null
  /** What links will actually use: the override or the deploy-time value. */
  effectiveBaseUrl: string
  allowPublicRegistration: boolean
  allowPublicSpaces: boolean
  emailEnabled: boolean
  smtpHost: string | null
  smtpPort: number
  smtpUsername: string | null
  /** The password itself is never returned: only whether one is stored. */
  smtpPasswordSet: boolean
  smtpFromAddress: string | null
  smtpTls: number
  requireTotpForAdmins: boolean
  /** Which parts of this the caller may change (dev-plan 11.1). */
  permissions: string[]
  /** Hosts an embed block may frame, one per line (dev-plan Phase 7 Wave E). */
  embedAllowlist: string
  /** Brute-force protection (dev-plan 3.2). Every limiter is tunable. */
  loginRateLimitPerMinute: number
  anonymousRateLimitPerMinute: number
  tokenMintLimitPerHour: number
  lockoutThreshold: number
  lockoutBaseSeconds: number
  lockoutMaxSeconds: number
  updatedAt: string
}

/** Every field optional: an omitted field keeps its stored value. */
export type SiteSettingsUpdate = Omit<SiteSettings, 'smtpPasswordSet' | 'updatedAt' | 'effectiveBaseUrl'> & {
  smtpPassword: string
}

export type AdminUser = {
  id: string
  email: string
  displayName: string
  role: UserRole
  /** Which role, not just which tier (dev-plan 11.2). */
  roleId: string | null
  roleName: string
  status: UserStatus
  avatarHash: string | null
  avatarVariant: number | null
  hasPassword: boolean
  isSso: boolean
  recoveryCodesRemaining: number
  recoveryCodesSaved: boolean
  /** Two-factor is on, so an administrator may turn it off (dev-plan 15.1). */
  totpEnabled: boolean
  lastSeenAt: string | null
  createdAt: string
  failedLoginCount: number
  /** Set while a temporary lockout is in force (dev-plan 3.2). */
  lockedUntil: string | null
}

export type LockoutRow = {
  userId: string
  email: string
  displayName: string
  failedLoginCount: number
  lockedUntil: string
}

export type SecurityLimits = {
  loginRateLimitPerMinute: number
  anonymousRateLimitPerMinute: number
  tokenMintLimitPerHour: number
  lockoutThreshold: number
  lockoutBaseSeconds: number
  lockoutMaxSeconds: number
  activeLockouts: LockoutRow[]
}

/** Matches Api.Domain.SecuritySeverity / SecurityAlertStatus. */
export const SecuritySeverity = { Info: 0, Warning: 1, Critical: 2 } as const
export type SecuritySeverity = (typeof SecuritySeverity)[keyof typeof SecuritySeverity]
export const AlertStatus = { Open: 0, Acknowledged: 1, Resolved: 2 } as const
export type AlertStatus = (typeof AlertStatus)[keyof typeof AlertStatus]

export type SecurityOverview = {
  openAlerts: number
  criticalOpen: number
  eventsLast24h: number
  blockedNetworks: number
  blockedHits: number
  allowPublicSpaces: boolean
  allowPublicRegistration: boolean
  requireTotpForAdmins: boolean
}

export type SecurityEvent = {
  id: string
  kind: string
  severity: SecuritySeverity
  key: string
  ip: string | null
  actorId: string | null
  actorName: string | null
  targetType: string | null
  targetId: string | null
  metadataJson: string | null
  createdAt: string
}

export type SecurityAlert = {
  id: string
  eventId: string
  kind: string
  severity: SecuritySeverity
  key: string
  ip: string | null
  actorId: string | null
  actorName: string | null
  status: AlertStatus
  createdAt: string
  acknowledgedAt: string | null
  acknowledgedByName: string | null
  resolvedAt: string | null
  resolvedByName: string | null
  note: string | null
  metadataJson: string | null
}

export type BlockedNetwork = {
  id: string
  cidr: string
  reason: string | null
  createdByName: string | null
  createdAt: string
  expiresAt: string | null
}

export type AuditChainReport = {
  ok: boolean
  checked: number
  unchained: number
  brokenAtSequence: number | null
  problem: string | null
  verifiedAt: string
}

export type AdminSpace = {
  isPublic: boolean
  publicComments: boolean
  publicSince: string | null
  attachmentCount: number
  id: string
  key: string
  name: string
  description: string | null
  archived: boolean
  createdById: string
  createdByName: string
  pageCount: number
  storageBytes: number
  createdAt: string
}

export type Invite = {
  id: string
  email: string | null
  expiresAt: string
  usedAt: string | null
  createdAt: string
  /** The account the invite created. */
  usedByName: string | null
}

export type DailyPoint = { date: string; count: number }

export type Dashboard = {
  rangeDays: number
  generatedAt: string
  people: {
    total: number
    admins: number
    suspended: number
    activeLast7Days: number
    activeLast30Days: number
    newInRange: number
    loginsPerDay: DailyPoint[]
    failedLoginsPerDay: DailyPoint[]
  }
  content: {
    spaces: number
    pages: number
    versions: number
    comments: number
    attachments: number
    storageBytes: number
    pagesCreatedPerDay: DailyPoint[]
  }
  usage: {
    viewsInRange: number
    viewsPerDay: DailyPoint[]
    topPages: { pageId: string; title: string; spaceKey: string; views: number }[]
    topEditors: { userId: string; displayName: string; versions: number }[]
  }
  /** Dev-plan 9.1: one entry per backup agent. */
  health: { backups: BackupHealth[] }
}

/** Backups (dev-plan 9.1). The two sidecars: pg_dump plus uploads, and pgBackRest. */
export type BackupAgentName = 'logical' | 'physical'

export type BackupPolicy = {
  enabled: boolean
  keepCount: number
  keepDays: number
  changedAt: string | null
  changedByName: string | null
}

export type Backup = {
  id: string
  agent: BackupAgentName
  /** Logical: the cycle's timestamp. Physical: pgBackRest's label. */
  label: string
  type: 'dump' | 'full' | 'diff' | 'incr'
  prior: string | null
  /** Physical incrementals: the full backup they belong to. */
  fullLabel: string | null
  startedAt: string
  completedAt: string | null
  sizeBytes: number
  hasUploads: boolean
  error: string | null
  removedAt: string | null
  removedReason: 'retention' | 'missing' | null
  lastVerifiedAt: string | null
  lastVerifyOk: boolean | null
  detailJson: string | null
}

export type BackupJob = {
  id: string
  agent: BackupAgentName
  kind: 'backup' | 'restore-test' | 'copy-offsite' | 'restore' | 'restore-undo' | 'restore-discard' | 'test-target'
  trigger: 'scheduled' | 'manual' | 'startup' | 'retention'
  status: 'requested' | 'running' | 'succeeded' | 'failed'
  target: string | null
  requestedAt: string
  requestedByName: string | null
  startedAt: string | null
  finishedAt: string | null
  error: string | null
  resultJson: string | null
  /** Only from `job(id)`. */
  logTail: string | null
}

/**
 * What a restore would replace, and what it would cost (dev-plan 9.4).
 * `blockedBy` is why the button is disabled; empty means it is allowed.
 */
export type RestorePreview = {
  label: string
  agent: BackupAgentName
  mode: 'logical' | 'pitr'
  backupAt: string
  targetAt: string | null
  earliestTarget: string | null
  latestTarget: string | null
  hasUploads: boolean
  backupBytes: number
  pagesCreated: number
  versionsSaved: number
  commentsPosted: number
  attachmentsAdded: number
  attachmentBytes: number
  accountsCreated: number
  sessionsEnding: number
  freeBytes: number | null
  neededBytes: number | null
  blockedBy: string[]
}

/** The confirmation every restore action takes: the typed word and the password. */
export type RestoreInput = {
  confirmLabel: string
  password?: string
  code?: string
  at?: string | null
}

export type RestoreQueued = { jobId: string; mode?: string; describes?: string }

/** The copy a restore replaced, kept so it can be undone (dev-plan 9.4). */
export type KeptCopy = {
  jobId: string
  mode: 'logical' | 'pitr'
  restoredAt: string
  database: string | null
  uploads: string | null
  restoredFrom: string | null
  databaseBytes: number | null
  uploadsBytes: number | null
  removedAt: string | null
}

export type BackupAgent = {
  name: BackupAgentName
  /** False until the sidecar has written its first row. */
  reporting: boolean
  online: boolean
  overdue: boolean
  lastRunFailed: boolean
  diskLow: boolean
  startedAt: string | null
  lastSeenAt: string | null
  nextRunAt: string | null
  intervalHours: number | null
  fullEveryDays: number | null
  toolVersion: string | null
  message: string | null
  volumeFreeBytes: number | null
  volumeTotalBytes: number | null
  walArchivedAt: string | null
  lastSuccess: Backup | null
  lastFailure: BackupJob | null
  successRate30Days: number | null
  presentCount: number
  presentBytes: number
  oldestRestorePoint: string | null
  lastVerifiedAt: string | null
  lastVerifyOk: boolean | null
  appliedPolicy: BackupPolicy | null
  /** When a stricter saved policy starts removing backups on this agent. */
  policyEffectiveAt: string | null
  policyPending: boolean
}

export type BackupOverview = {
  policy: BackupPolicy
  agents: BackupAgent[]
  backups: Backup[]
  removed: Backup[]
  jobs: BackupJob[]
  targets: BackupTarget[]
  offsiteIsManualOnly: boolean
  disks: DiskChart[]
  restore: RestoreStatus
}

/** Where the instance stands on restores (dev-plan 9.4). */
export type RestoreStatus = {
  jobId: string | null
  startedAt: string | null
  cancelRequested: boolean
  lastRestoredAt: string | null
  lastRestoreFrom: string | null
  keptCopy: KeptCopy | null
  /** When the retention policy will take the kept copy, if it will. */
  keptCopyExpiresAt: string | null
}

/**
 * The space one disk holds (dev-plan 9.3). One per filesystem rather than
 * per agent: both backup agents normally write to the same disk, and drawing
 * it twice would double its free space on the screen.
 */
export type DiskChart = {
  filesystem: string | null
  agents: string[]
  backupBytes: number
  wikiBytes: number
  otherBytes: number
  freeBytes: number
  totalBytes: number
  low: boolean
  measuredAt: string | null
}

/**
 * One offsite target (dev-plan 9.2), as the backup sidecars published it.
 * Keys and passphrases are fingerprints and nothing else: the credentials
 * live in .env, are read only by the sidecars, and the app never holds one.
 */
export type BackupTarget = {
  slot: 'cloud' | 'nas' | 'removable'
  kind: 'database' | 'files'
  type: string | null
  location: string | null
  bucket: string | null
  prefix: string | null
  enabled: boolean
  present: boolean | null
  problem: string | null
  message: string | null
  keyFingerprint: string | null
  passphraseFingerprint: string | null
  lastBackupAt: string | null
  lastWalAt: string | null
  lastVerifyAt: string | null
  lastDrillAt: string | null
  lastDrillOk: boolean | null
  bytesStored: number | null
  walBacklogFiles: number | null
  /** OFFSITE_CLOUD_BUDGET_GB in bytes, when someone set it. A chosen number, not a measured one. */
  budgetBytes: number | null
  updatedAt: string
}

export type BackupPreview = {
  stricter: boolean
  agents: {
    agent: BackupAgentName
    removed: Backup[]
    removedBytes: number
    oldestRestorePoint: string | null
    newOldestRestorePoint: string | null
    stricter: boolean
  }[]
}

export type BackupHealth = {
  agent: BackupAgentName
  reporting: boolean
  online: boolean
  overdue: boolean
  lastRunFailed: boolean
  lastBackupAt: string | null
  lastBackupBytes: number | null
}

export type BackupPolicyInput = { enabled: boolean; keepCount: number; keepDays: number }

/** A dynamic block's answer, one of three neutral shapes (architecture.md, "Dynamic blocks"). */
export type BlockUser = { id: string; displayName: string; avatarHash: string | null; avatarVariant: number | null }
export type BlockCell = { text?: string | null; href?: string | null; date?: string | null; user?: BlockUser | null; checked?: boolean | null }
export type BlockItem = { title: string; href?: string | null; subtitle?: string | null; cells?: Record<string, BlockCell> | null; children?: BlockItem[] | null }
export type BlockResult = {
  kind: string
  shape: 'list' | 'table' | 'document'
  items: BlockItem[]
  title?: string | null
  empty?: string | null
  columns?: { key: string; label: string }[] | null
  document?: string | null
  generatedAt: string
}

export type Directory = {
  id: string
  email: string
  displayName: string
  avatarHash: string | null
  avatarVariant: number | null
}

export type SpacePermission = {
  id: string
  principalType: number
  principalId: string
  principalName: string | null
  operation: number
}

export type PageRestriction = {
  id: string
  principalType: number
  principalId: string
  principalName: string | null
  operation: number
}

export type ApiTokenSummary = {
  id: string
  name: string
  prefix: string
  createdAt: string
  lastUsedAt: string | null
  /** A read-only token cannot change anything (dev-plan 8.4). */
  readOnly: boolean
}
export type CreatedApiToken = ApiTokenSummary & { token: string }

export type Webhook = { id: string; url: string; events: string; enabled: boolean; createdAt: string }
export type CreatedWebhook = Webhook & { secret: string }

export type WatchStatus = { watching: boolean }

export type AppNotification = {
  id: string
  action: string
  targetType: 'page' | 'space' | 'security'
  targetId: string
  actorId: string | null
  actorName: string | null
  metadataJson: string | null
  createdAt: string
  readAt: string | null
}

export type AuditEntry = {
  id: string
  action: string
  targetType: string
  targetId: string | null
  actorId: string | null
  actorName: string | null
  metadataJson: string | null
  createdAt: string
}

export type Label = { id: string; name: string }
export type LabelUsage = { id: string; name: string; pageCount: number }
export type LabeledPage = { pageId: string; spaceId: string; spaceKey: string; title: string }

export type SearchResult = {
  pageId: string
  spaceId: string
  spaceKey: string
  title: string
  snippet: string
}

export type Comment = {
  id: string
  pageId: string
  parentCommentId: string | null
  body: string | null
  anchorJson: string | null
  authorId: string
  authorName: string
  authorAvatarHash: string | null
  authorAvatarVariant: number | null
  isInline: boolean
  isDeleted: boolean
  createdAt: string
  updatedAt: string
  /** Set on a resolved thread's first comment (dev-plan 15.3). */
  resolvedAt?: string | null
  resolvedByName?: string | null
}

/** An error carrying the HTTP status and any field validation messages. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>
  /** A machine-readable reason, e.g. `reauth_required`. */
  readonly code: string | null
  /** The parsed error body, for the fields that are particular to one
   *  endpoint: setup's 409 naming the step still outstanding, say. */
  readonly details: Record<string, unknown>

  constructor(
    status: number, message: string, fieldErrors: Record<string, string[]> = {},
    code: string | null = null, details: Record<string, unknown> = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
    this.code = code
    this.details = details
  }
}

type Body = object | undefined

/** Marks a request as coming from this page. The server refuses cookie-
 *  authenticated state changes without it: a cross-site page cannot add a
 *  custom header without a CORS preflight it will never pass. */
const CSRF_HEADER = { 'X-Requested-With': 'Tesria' }

async function request<T>(method: string, path: string, body?: Body): Promise<T> {
  const send = () => fetch(path, {
    method,
    credentials: 'include',
    headers: body !== undefined ? { ...CSRF_HEADER, 'Content-Type': 'application/json' } : CSRF_HEADER,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  try {
    return await handle<T>(await send())
  } catch (err) {
    // Sudo mode (dev-plan 3.5): the server wants the password confirmed
    // before this action. Ask, then retry once; a cancel rejects as usual.
    if (err instanceof ApiError && err.code === 'reauth_required') {
      await requestReauth()
      return handle<T>(await send())
    }
    throw err
  }
}

/**
 * A multipart upload, with the same sudo handling as `request`: branding
 * uploads (dev-plan 13.1) need a fresh sign-in, and a file picked a minute
 * after the sudo window closed should prompt for the password and go
 * through, not fail.
 */
async function upload<T>(method: string, path: string, file: Blob, name = 'file'): Promise<T> {
  const send = () => {
    const body = new FormData()
    body.append('file', file, file instanceof File ? file.name : name)
    return fetch(path, { method, credentials: 'include', headers: CSRF_HEADER, body })
  }
  try {
    return await handle<T>(await send())
  } catch (err) {
    if (err instanceof ApiError && err.code === 'reauth_required') {
      await requestReauth()
      return handle<T>(await send())
    }
    throw err
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = text ? safeJson(text) : undefined

  if (!res.ok) {
    // ASP.NET Core ValidationProblem shape: { errors: { field: [msgs] } }.
    const fieldErrors = (data && typeof data === 'object' && 'errors' in data
      ? (data as { errors: Record<string, string[]> }).errors
      : {}) as Record<string, string[]>
    // `detail` is where Results.Problem puts its explanation. It used to be
    // skipped, so "Registration is by invitation on this instance." reached
    // people as "You do not have permission to do that." and a rate limit
    // as "Request failed (429)." (found writing the support site, 2026-09-23).
    const field = (name: string) =>
      data && typeof data === 'object' && name in data && (data as Record<string, unknown>)[name]
        ? String((data as Record<string, unknown>)[name])
        : undefined
    const message =
      field('message') ??
      field('detail') ??
      firstFieldError(fieldErrors) ??
      defaultMessage(res.status)
    const code = data && typeof data === 'object' && 'code' in data
      ? String((data as { code: unknown }).code)
      : null
    const details = (data && typeof data === 'object' ? data : {}) as Record<string, unknown>
    // A restore is running and the wiki is read-only (dev-plan 9.4).
    // Announced once, here, rather than handled at every call site: every
    // write in the product gets the same answer, and the overlay is the only
    // honest thing to show somebody whose save just did not happen.
    if (res.status === 503 && code === 'maintenance') {
      window.dispatchEvent(new CustomEvent('tesria:maintenance', { detail: details.maintenance }))
    }
    throw new ApiError(res.status, message, fieldErrors, code, details)
  }
  return data as T
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return undefined
  }
}

function firstFieldError(errors: Record<string, string[]>): string | undefined {
  for (const messages of Object.values(errors)) if (messages?.length) return messages[0]
  return undefined
}

function defaultMessage(status: number): string {
  if (status === 401) return 'You need to sign in.'
  if (status === 403) return 'You do not have permission to do that.'
  if (status === 404) return 'Not found.'
  if (status === 409) return 'Conflict.'
  if (status === 413) return 'That file is too large to upload here.'
  if (status === 429) return 'Too many attempts. Wait a minute and try again.'
  return `Request failed (${status}).`
}

export const api = {
  auth: {
    me: () => request<User>('GET', '/api/auth/me'),
    login: (email: string, password: string) =>
      request<User | TotpChallenge>('POST', '/api/auth/login', { email, password }),
    loginTotp: (challenge: string, code: string) =>
      request<User>('POST', '/api/auth/login/totp', { challenge, code }),
    /** Confirms the password (or a code) for sudo mode. */
    reauth: (input: { password?: string; code?: string }) =>
      request<void>('POST', '/api/auth/reauth', input),
    setNotificationPreference: (emailNotifications: EmailNotificationMode) =>
      request<User>('PUT', '/api/auth/me/notifications', { emailNotifications }),
    sessions: {
      list: () => request<Session[]>('GET', '/api/auth/me/sessions'),
      revoke: (id: string) => request<void>('DELETE', `/api/auth/me/sessions/${id}`),
      revokeOthers: () => request<void>('DELETE', '/api/auth/me/sessions/others'),
    },
    totp: {
      /** `currentPassword` may be omitted within the fresh-login window. */
      setup: (input: { currentPassword?: string }) =>
        request<TotpSetup>('POST', '/api/auth/me/totp/setup', input),
      enable: (code: string) => request<User>('POST', '/api/auth/me/totp/enable', { code }),
      disable: (input: { currentPassword?: string; code?: string }) =>
        request<User>('POST', '/api/auth/me/totp/disable', input),
    },
    /** Returns the user plus the recovery codes: the one moment they exist. */
    register: (email: string, displayName: string, password: string, inviteToken?: string) =>
      request<User & { recoveryCodes: string[] }>('POST', '/api/auth/register', {
        email,
        displayName,
        password,
        inviteToken,
      }),
    logout: () => request<void>('POST', '/api/auth/logout'),
    oidcStatus: () => request<{ enabled: boolean; displayName: string }>('GET', '/api/auth/oidc/status'),
    /** "I have saved these" on the recovery codes (dev-plan 10.2). */
    acknowledgeRecoveryCodes: () => request<void>('POST', '/api/auth/me/recovery-codes/acknowledge'),
    /** The tour and tips (dev-plan 10.3). */
    updateOnboarding: (input: OnboardingUpdate) =>
      request<OnboardingSummary>('PUT', '/api/auth/me/onboarding', input),
    updateProfile: (input: { displayName: string }) =>
      request<User>('PUT', '/api/auth/me', input),
    changeEmail: (input: { currentPassword: string; email: string }) =>
      request<User>('PUT', '/api/auth/me/email', input),
    changePassword: (input: { currentPassword: string; newPassword: string }) =>
      request<User>('PUT', '/api/auth/me/password', input),
    recoveryStatus: () =>
      request<{ remaining: number }>('GET', '/api/auth/me/recovery-codes'),
    /** `currentPassword` may be omitted within the fresh-login window. */
    regenerateRecoveryCodes: (input: { currentPassword?: string }) =>
      request<{ codes: string[] }>('POST', '/api/auth/me/recovery-codes', input),
    recoveryOptions: () => request<{ emailEnabled: boolean }>('GET', '/api/auth/recovery-options'),
    recoverByEmail: (email: string) =>
      request<{ message: string }>('POST', '/api/auth/recover/email', { email }),
    recoverWithCode: (input: { email: string; code: string; newPassword: string }) =>
      request<void>('POST', '/api/auth/recover/code', input),
    resetWithToken: (input: { token: string; newPassword: string }) =>
      request<void>('POST', '/api/auth/recover/token', input),
  },
  instance: () => request<InstanceInfo>('GET', '/api/instance'),
  setup: {
    status: () => request<SetupStatus>('GET', '/api/setup'),
    /** Records that a step was answered. The server refuses to record a required one as skipped. */
    recordStep: (key: string, skipped = false) =>
      request<SetupStatus>('POST', `/api/setup/steps/${encodeURIComponent(key)}`, { skipped }),
    /** Checked against evidence server-side; 409 names the step still outstanding. */
    complete: () => request<SetupStatus>('POST', '/api/setup/complete'),
  },
  spaces: {
    list: (includeArchived = false) =>
      request<Space[]>('GET', `/api/spaces?includeArchived=${includeArchived}`),
    get: (key: string) => request<Space>('GET', `/api/spaces/${encodeURIComponent(key)}`),
    create: (input: { key: string; name: string; description?: string | null }) =>
      request<Space>('POST', '/api/spaces', input),
    update: (
      key: string,
      input: {
        name: string
        description?: string | null
        iconKind?: SpaceIconKind
        iconValue?: string | null
        iconColor?: number | null
        treeStyle?: SpaceTreeStyle
      },
    ) => request<Space>('PUT', `/api/spaces/${encodeURIComponent(key)}`, input),
    /** The picture case, which needs the bytes rather than JSON. */
    uploadIcon: async (key: string, blob: Blob): Promise<{ iconHash: string }> => {
      const body = new FormData()
      body.append('file', blob, 'icon.png')
      const res = await fetch(`/api/media/space-icons/${encodeURIComponent(key)}`, {
        method: 'PUT',
        credentials: 'include',
        headers: CSRF_HEADER,
        body,
      })
      return handle<{ iconHash: string }>(res)
    },
    removeIcon: (key: string) =>
      request<void>('DELETE', `/api/media/space-icons/${encodeURIComponent(key)}`),
    archive: (key: string) =>
      request<Space>('POST', `/api/spaces/${encodeURIComponent(key)}/archive`, {}),
    /** Turn this space's export formats on and off (dev-plan 12.3). */
    setExports: (key: string, exports: SpaceExports) =>
      request<Space>('PUT', `/api/spaces/${encodeURIComponent(key)}/exports`, exports),
    unarchive: (key: string) =>
      request<Space>('POST', `/api/spaces/${encodeURIComponent(key)}/unarchive`, {}),
    /** What the delete dialog counts up before asking (dev-plan 11.3). */
    deletionPreview: (key: string) =>
      request<SpaceDeletionPreview>('GET', `/api/spaces/${encodeURIComponent(key)}/deletion-preview`),
    /** Irreversible. The key proves the space, the password proves the person. */
    remove: (key: string, input: { confirmKey: string; password?: string; code?: string }) =>
      request<void>('DELETE', `/api/spaces/${encodeURIComponent(key)}`, input),
    /** The whole space as a static site, as a zip (dev-plan 12.2). */
    exportSite: async (key: string, audience: 'anonymous' | 'me'): Promise<Blob> => {
      const res = await fetch(
        `/api/spaces/${encodeURIComponent(key)}/export/site?audience=${audience}`,
        { credentials: 'include', headers: CSRF_HEADER },
      )
      if (!res.ok) return handle<Blob>(res)
      return res.blob()
    },
    /** What an import turned out to contain, and what it could not carry. */
    importPack: async (file: File, key: string, name?: string): Promise<ImportedPack> => {
      const body = new FormData()
      body.append('file', file, file.name)
      body.append('key', key)
      if (name) body.append('name', name)
      const res = await fetch('/api/spaces/import', {
        method: 'POST',
        credentials: 'include',
        headers: CSRF_HEADER,
        body,
      })
      return handle<ImportedPack>(res)
    },
    /**
     * The whole space as a wiki pack, as a zip (dev-plan 8.5).
     *
     * No audience to choose, unlike a site: a pack is for reading back into
     * Tesria, so it carries everything you can see and nothing you cannot.
     */
    exportPack: async (key: string): Promise<Blob> => {
      const res = await fetch(`/api/spaces/${encodeURIComponent(key)}/export/pack`, {
        credentials: 'include',
        headers: CSRF_HEADER,
      })
      if (!res.ok) return handle<Blob>(res)
      return res.blob()
    },
  },
  pages: {
    tree: (spaceId: string) => request<PageTreeNode[]>('GET', `/api/pages/tree?spaceId=${spaceId}`),
    get: (id: string) => request<PageDetail>('GET', `/api/pages/${id}`),
    create: (input: {
      spaceId: string
      parentPageId?: string | null
      title: string
      contentJson?: string
    }) => request<PageDetail>('POST', '/api/pages', input),
    update: (
      id: string,
      input: {
        title?: string | null
        contentJson: string
        changeComment?: string | null
        /**
         * Which published version this edit started from (dev-plan 8.6).
         * Sending it turns a publish that would overwrite an unseen change
         * into a 409 carrying the page as it stands. Omit it for
         * last-write-wins.
         */
        baseVersion?: number | null
      },
    ) => request<PageDetail>('PUT', `/api/pages/${id}`, input),
    /** spaceId moves the page, and the pages under it, to another space (dev-plan 15.3). */
    move: (id: string, input: { parentPageId?: string | null; index: number; spaceId?: string | null }) =>
      request<void>('PUT', `/api/pages/${id}/move`, input),
    copy: (id: string, input: { spaceId?: string | null; parentPageId?: string | null; includeChildren: boolean }) =>
      request<{ id: string; spaceId: string; title: string; pages: number }>('POST', `/api/pages/${id}/copy`, input),
    setLayout: (id: string, input: { fullWidth: boolean }) =>
      request<void>('PUT', `/api/pages/${id}/layout`, input),
    /** An emoji before the title, or null to take it away (dev-plan 15.7). */
    setEmoji: (id: string, emoji: string | null) =>
      request<void>('PUT', `/api/pages/${id}/emoji`, { emoji }),
    remove: (id: string) => request<void>('DELETE', `/api/pages/${id}`),
    createDraft: (input: { spaceId: string; parentPageId?: string | null }) =>
      request<{ id: string }>('POST', '/api/pages/draft', input),
    publish: (id: string, input: { title: string; contentJson: string }) =>
      request<PageDetail>('POST', `/api/pages/${id}/publish`, input),
    deleteDraft: (id: string) => request<void>('DELETE', `/api/pages/${id}/draft`),
    trash: (spaceId: string) => request<TrashedPage[]>('GET', `/api/pages/trash?spaceId=${spaceId}`),
    untrash: (id: string) => request<void>('POST', `/api/pages/${id}/restore`),
    purge: (id: string) => request<void>('DELETE', `/api/pages/${id}/purge`),
    /** Short-lived token authorizing this user to join the page's live session. */
    collabToken: (id: string) => request<CollabToken>('GET', `/api/pages/${id}/collab-token`),
    versions: (id: string) => request<VersionMeta[]>('GET', `/api/pages/${id}/versions`),
    version: (id: string, n: number) =>
      request<VersionContent>('GET', `/api/pages/${id}/versions/${n}`),
    restore: (id: string, n: number) =>
      request<PageDetail>('POST', `/api/pages/${id}/versions/${n}/restore`, {}),
  },
  attachments: {
    listForPage: (pageId: string) =>
      request<Attachment[]>('GET', `/api/pages/${pageId}/attachments`),
    upload: async (pageId: string, file: File): Promise<Attachment> => {
      const form = new FormData()
      form.append('file', file)
      const res = await fetch(`/api/pages/${pageId}/attachments`, {
        method: 'POST',
        credentials: 'include',
        headers: CSRF_HEADER,
        body: form,
      })
      return handle<Attachment>(res)
    },
    remove: (id: string) => request<void>('DELETE', `/api/attachments/${id}`),
    downloadUrl: (id: string) => `/api/attachments/${id}/download`,
  },
  users: {
    list: () => request<Directory[]>('GET', '/api/users'),
  },
  embeds: {
    /** What, if anything, this address may become in a frame: the server decides. */
    resolve: (url: string) =>
      request<EmbedResolution>('GET', `/api/embeds/resolve?url=${encodeURIComponent(url)}`),
    unfurl: (url: string) =>
      request<LinkPreview>('GET', `/api/embeds/unfurl?url=${encodeURIComponent(url)}`),
  },
  blocks: {
    get: (hostPageId: string, kind: string, params: Record<string, string>) => {
      const qs = new URLSearchParams(params).toString()
      return request<BlockResult>('GET', `/api/pages/${hostPageId}/blocks/${encodeURIComponent(kind)}${qs ? `?${qs}` : ''}`)
    },
  },
  admin: {
    settings: {
      get: () => request<SiteSettings>('GET', '/api/admin/settings'),
      update: (input: Partial<SiteSettingsUpdate>) =>
        request<SiteSettings>('PUT', '/api/admin/settings', input),
      sendTestEmail: () =>
        request<{ sent: boolean; error: string | null }>('POST', '/api/admin/settings/email/test'),
    },
    users: {
      list: () => request<AdminUser[]>('GET', '/api/admin/users'),
      /** A tier: the promotion path (dev-plan 10.1). */
      setRole: (id: string, role: UserRole) =>
        request<AdminUser>('PUT', `/api/admin/users/${id}/role`, { role }),
      /** A specific role (dev-plan 11.2). Crossing tiers follows the same rules as a promotion. */
      assignRole: (id: string, roleId: string) =>
        request<AdminUser>('PUT', `/api/admin/users/${id}/role`, { roleId }),
      /** Owner only, and sudo: the caller becomes an administrator. */
      transferOwnership: (id: string) =>
        request<AdminUser>('POST', `/api/admin/users/${id}/transfer-ownership`),
      setStatus: (id: string, status: UserStatus) =>
        request<AdminUser>('PUT', `/api/admin/users/${id}/status`, { status }),
      revokeSessions: (id: string) =>
        request<void>('POST', `/api/admin/users/${id}/revoke-sessions`),
      revokeTokens: (id: string) =>
        request<void>('POST', `/api/admin/users/${id}/revoke-tokens`),
      unlock: (id: string) => request<void>('POST', `/api/admin/users/${id}/unlock`),
      /** Sudo; never the owner; another administrator only by the owner (dev-plan 15.1). */
      disableTwoFactor: (id: string) =>
        request<void>('POST', `/api/admin/users/${id}/disable-two-factor`),
      issueReset: (id: string) =>
        request<{ token: string; path: string; expiresAt: string }>(
          'POST', `/api/admin/users/${id}/reset-password`),
    },
    spaces: {
      list: () => request<AdminSpace[]>('GET', '/api/admin/spaces'),
      /** Admin access to a space they hold no grant for; audited, and a no-op on an open space. */
      recoverAccess: (key: string) =>
        request<{ spaceId: string; key: string; name: string; alreadyHadAccess: boolean }>(
          'POST', `/api/admin/spaces/${encodeURIComponent(key)}/recover-access`, {}),
      setPublic: (key: string, input: { isPublic: boolean; publicComments?: boolean }) =>
        request<AdminSpace>('PUT', `/api/admin/spaces/${key}/public`, input),
    },
    invites: {
      list: () => request<Invite[]>('GET', '/api/admin/invites'),
      create: (input: { email?: string; expiresInDays?: number }) =>
        request<{ token: string; path: string; email: string | null; expiresAt: string }>(
          'POST', '/api/admin/invites', input),
      revoke: (id: string) => request<void>('DELETE', `/api/admin/invites/${id}`),
    },
    dashboard: (rangeDays: number) =>
      request<Dashboard>('GET', `/api/admin/dashboard?rangeDays=${rangeDays}`),
    roles: {
      matrix: () => request<PermissionMatrix>('GET', '/api/admin/roles'),
      /** Sudo: the client asks for the password if the session is past the window. */
      savePermissions: (roleId: string, permissions: string[]) =>
        request<{ id: string; permissions: string[] }>('PUT', `/api/admin/roles/${roleId}/permissions`, { permissions }),
      reset: (roleId: string) =>
        request<{ id: string; permissions: string[] }>('POST', `/api/admin/roles/${roleId}/reset`),
      review: () => request<void>('POST', '/api/admin/roles/review'),
      /** Custom roles (dev-plan 11.2). Starts as a copy of `copyFrom`, or of the tier's built-in. */
      create: (input: { name: string; description?: string; tier: UserRole; copyFrom?: string }) =>
        request<InstanceRole>('POST', '/api/admin/roles', input),
      rename: (roleId: string, input: { name: string; description?: string }) =>
        request<{ id: string; name: string }>('PUT', `/api/admin/roles/${roleId}`, input),
      /** Sudo. Refused while anyone still holds the role. */
      remove: (roleId: string) => request<void>('DELETE', `/api/admin/roles/${roleId}`),
    },
    /** Instance branding (dev-plan 13.1). Every change is sudo. */
    branding: {
      get: () => request<BrandingSettings>('GET', '/api/admin/branding'),
      save: (input: BrandingInput) => request<BrandingSettings>('PUT', '/api/admin/branding', input),
      preview: (light: string | null, dark: string | null) =>
        request<AccentCheck[]>('POST', '/api/admin/branding/accent-preview', { light, dark }),
      reset: () => request<BrandingSettings>('POST', '/api/admin/branding/reset', {}),
      uploadLogo: (file: File, dark = false) =>
        upload<BrandingSettings>('PUT', `/api/admin/branding/${dark ? 'logo-dark' : 'logo'}`, file),
      removeLogo: (dark = false) =>
        request<BrandingSettings>('DELETE', `/api/admin/branding/${dark ? 'logo-dark' : 'logo'}`),
      uploadFavicon: (file: File) => upload<BrandingSettings>('PUT', '/api/admin/branding/favicon', file),
      removeFavicon: () => request<BrandingSettings>('DELETE', '/api/admin/branding/favicon'),
    },
    backups: {
      overview: (includeRemoved = false) =>
        request<BackupOverview>('GET', `/api/admin/backups${includeRemoved ? '?includeRemoved=true' : ''}`),
      /** Sudo: the client asks for the password if the session is past the window. */
      savePolicy: (input: BackupPolicyInput) => request<BackupPolicy>('PUT', '/api/admin/backups/policy', input),
      preview: (input: BackupPolicyInput) => request<BackupPreview>('POST', '/api/admin/backups/policy/preview', input),
      run: (agents?: BackupAgentName[]) => request<BackupJob[]>('POST', '/api/admin/backups/run', { agents }),
      /** Copies to a target that is only there sometimes (a drive, 9.2 step 4). */
      copyToTarget: (slot: string) =>
        request<BackupJob>('POST', `/api/admin/backups/targets/${encodeURIComponent(slot)}/copy`, {}),
      /** Test connection: one job per repository the slot holds, answered by the sidecars. */
      testTarget: (slot: string) =>
        request<BackupJob[]>('POST', `/api/admin/backups/targets/${encodeURIComponent(slot)}/test`, {}),
      restoreTest: (label: string) =>
        request<BackupJob>('POST', `/api/admin/backups/${encodeURIComponent(label)}/restore-test`),
      job: (id: string) => request<BackupJob>('GET', `/api/admin/backups/jobs/${id}`),
      /** Replacing the wiki with an older copy (dev-plan 9.4). */
      restorePreview: (label: string, at?: string) =>
        request<RestorePreview>(
          'GET',
          `/api/admin/backups/${encodeURIComponent(label)}/restore-preview${at ? `?at=${encodeURIComponent(at)}` : ''}`,
        ),
      restore: (label: string, input: RestoreInput) =>
        request<RestoreQueued>('POST', `/api/admin/backups/${encodeURIComponent(label)}/restore`, input),
      cancelRestore: () =>
        request<{ canceled: boolean; message: string }>('POST', '/api/admin/backups/restore/cancel', {}),
      undoRestore: (input: RestoreInput) =>
        request<RestoreQueued>('POST', '/api/admin/backups/restore/undo', input),
      discardKept: (input: RestoreInput) =>
        request<RestoreQueued>('POST', '/api/admin/backups/restore/discard-kept', input),
    },
    security: {
      limits: () => request<SecurityLimits>('GET', '/api/admin/security/limits'),
      verifyAuditChain: () => request<AuditChainReport>('POST', '/api/admin/audit/verify'),
      overview: () => request<SecurityOverview>('GET', '/api/admin/security/overview'),
      events: (take = 100) => request<SecurityEvent[]>('GET', `/api/admin/security/events?take=${take}`),
      alerts: (status: 'open' | 'all' = 'open') =>
        request<SecurityAlert[]>('GET', `/api/admin/security/alerts?status=${status}`),
      acknowledge: (id: string, note?: string) =>
        request<SecurityAlert>('POST', `/api/admin/security/alerts/${id}/acknowledge`, { note }),
      resolve: (id: string, note?: string) =>
        request<SecurityAlert>('POST', `/api/admin/security/alerts/${id}/resolve`, { note }),
      blocks: {
        list: () => request<BlockedNetwork[]>('GET', '/api/admin/security/blocks'),
        add: (input: { cidr: string; reason?: string; expiresInHours?: number }) =>
          request<BlockedNetwork>('POST', '/api/admin/security/blocks', input),
        remove: (id: string) => request<void>('DELETE', `/api/admin/security/blocks/${id}`),
      },
    },
  },
  avatar: {
    /** multipart upload; the server re-encodes to a 256px WebP square.
     *  Not via `request`, which sets a JSON content type: FormData must set
     *  its own multipart boundary. `handle` still parses the server's
     *  ValidationProblem body, so rejection messages surface as they do
     *  everywhere else. */
    upload: async (blob: Blob): Promise<{ avatarHash: string }> => {
      const body = new FormData()
      body.append('file', blob, 'avatar.png')
      const res = await fetch('/api/media/avatars/me', {
        method: 'PUT',
        credentials: 'include',
        headers: CSRF_HEADER,
        body,
      })
      return handle<{ avatarHash: string }>(res)
    },
    remove: () => request<void>('DELETE', '/api/media/avatars/me'),
    setVariant: (variant: number | null) =>
      request<void>('PUT', '/api/media/avatars/me/variant', { variant }),
  },
  apiTokens: {
    list: () => request<ApiTokenSummary[]>('GET', '/api/api-tokens'),
    create: (name: string, readOnly = false) =>
      request<CreatedApiToken>('POST', '/api/api-tokens', { name, readOnly }),
    revoke: (id: string) => request<void>('DELETE', `/api/api-tokens/${id}`),
  },
  webhooks: {
    list: (key: string) => request<Webhook[]>('GET', `/api/spaces/${encodeURIComponent(key)}/webhooks`),
    create: (key: string, input: { url: string; events: string }) =>
      request<CreatedWebhook>('POST', `/api/spaces/${encodeURIComponent(key)}/webhooks`, input),
    remove: (key: string, id: string) =>
      request<void>('DELETE', `/api/spaces/${encodeURIComponent(key)}/webhooks/${id}`),
  },
  pageWatch: {
    status: (pageId: string) => request<WatchStatus>('GET', `/api/pages/${pageId}/watch`),
    watch: (pageId: string) => request<void>('POST', `/api/pages/${pageId}/watch`),
    unwatch: (pageId: string) => request<void>('DELETE', `/api/pages/${pageId}/watch`),
  },
  spaceWatch: {
    status: (key: string) => request<WatchStatus>('GET', `/api/spaces/${encodeURIComponent(key)}/watch`),
    watch: (key: string) => request<void>('POST', `/api/spaces/${encodeURIComponent(key)}/watch`),
    unwatch: (key: string) => request<void>('DELETE', `/api/spaces/${encodeURIComponent(key)}/watch`),
  },
  notifications: {
    list: (unreadOnly?: boolean) =>
      request<AppNotification[]>('GET', `/api/notifications${unreadOnly ? '?unreadOnly=true' : ''}`),
    unreadCount: () => request<{ count: number }>('GET', '/api/notifications/unread-count'),
    markRead: (id: string) => request<void>('POST', `/api/notifications/${id}/read`),
    markAllRead: () => request<void>('POST', '/api/notifications/read-all'),
  },
  templates: {
    list: (spaceId?: string) =>
      request<PageTemplate[]>('GET', `/api/templates${spaceId ? `?spaceId=${spaceId}` : ''}`),
    create: (input: { spaceId?: string | null; name: string; description?: string | null; contentJson: string }) =>
      request<PageTemplate>('POST', '/api/templates', input),
    update: (id: string, input: { name: string; description?: string | null }) =>
      request<PageTemplate>('PUT', `/api/templates/${id}`, input),
    remove: (id: string) => request<void>('DELETE', `/api/templates/${id}`),
  },
  groups: {
    list: () => request<Group[]>('GET', '/api/groups'),
    create: (input: { name: string; description?: string | null }) =>
      request<Group>('POST', '/api/groups', input),
    update: (id: string, input: { name: string; description?: string | null }) =>
      request<Group>('PUT', `/api/groups/${id}`, input),
    remove: (id: string) => request<void>('DELETE', `/api/groups/${id}`),
    members: (id: string) => request<GroupMember[]>('GET', `/api/groups/${id}/members`),
    addMember: (id: string, userId: string) =>
      request<void>('POST', `/api/groups/${id}/members`, { userId }),
    removeMember: (id: string, userId: string) =>
      request<void>('DELETE', `/api/groups/${id}/members/${userId}`),
  },
  spacePermissions: {
    list: (key: string) =>
      request<SpacePermission[]>('GET', `/api/spaces/${encodeURIComponent(key)}/permissions`),
    grant: (key: string, input: { principalType: number; principalId: string; operation: number }) =>
      request<void>('POST', `/api/spaces/${encodeURIComponent(key)}/permissions`, input),
    revoke: (key: string, id: string) =>
      request<void>('DELETE', `/api/spaces/${encodeURIComponent(key)}/permissions/${id}`),
    /** Removes every grant: the space becomes open (dev-plan 15.3). Sudo. */
    makeOpen: (key: string) => request<void>('DELETE', `/api/spaces/${encodeURIComponent(key)}/permissions`),
  },
  pageRestrictions: {
    list: (pageId: string) => request<PageRestriction[]>('GET', `/api/pages/${pageId}/restrictions`),
    add: (pageId: string, input: { principalType: number; principalId: string; operation: number }) =>
      request<void>('POST', `/api/pages/${pageId}/restrictions`, input),
    remove: (pageId: string, id: string) =>
      request<void>('DELETE', `/api/pages/${pageId}/restrictions/${id}`),
  },
  audit: (params?: { targetType?: string; targetId?: string; take?: number }) => {
    const q = new URLSearchParams()
    if (params?.targetType) q.set('targetType', params.targetType)
    if (params?.targetId) q.set('targetId', params.targetId)
    if (params?.take) q.set('take', String(params.take))
    const suffix = q.toString()
    return request<AuditEntry[]>('GET', `/api/audit${suffix ? `?${suffix}` : ''}`)
  },
  labels: {
    all: () => request<LabelUsage[]>('GET', '/api/labels'),
    forPage: (pageId: string) => request<Label[]>('GET', `/api/pages/${pageId}/labels`),
    add: (pageId: string, name: string) =>
      request<Label>('POST', `/api/pages/${pageId}/labels`, { name }),
    remove: (pageId: string, name: string) =>
      request<void>('DELETE', `/api/pages/${pageId}/labels/${encodeURIComponent(name)}`),
    pages: (name: string) =>
      request<LabeledPage[]>('GET', `/api/labels/${encodeURIComponent(name)}/pages`),
  },
  search: (q: string, spaceId?: string) =>
    request<SearchResult[]>(
      'GET',
      `/api/search?q=${encodeURIComponent(q)}${spaceId ? `&spaceId=${spaceId}` : ''}`,
    ),
  comments: {
    listForPage: (pageId: string) => request<Comment[]>('GET', `/api/pages/${pageId}/comments`),
    create: (pageId: string, input: { body: string; parentCommentId?: string | null; anchorJson?: string | null }) =>
      request<Comment>('POST', `/api/pages/${pageId}/comments`, input),
    update: (id: string, body: string) => request<Comment>('PUT', `/api/comments/${id}`, { body }),
    remove: (id: string) => request<void>('DELETE', `/api/comments/${id}`),
    resolve: (id: string) => request<Comment>('POST', `/api/comments/${id}/resolve`),
    reopen: (id: string) => request<Comment>('POST', `/api/comments/${id}/reopen`),
  },
}
