import { requestReauth } from '../auth/reauth'

// Typed client for the Tesria REST API. All calls are same-origin and
// send the auth cookie automatically (credentials: 'include' for dev CORS).

/** Matches Api.Domain.UserRole. A const object rather than a TS `enum`:
 *  this project builds with `erasableSyntaxOnly`, which rejects enums. */
export const UserRole = { Member: 0, Admin: 1 } as const
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
   *  `avatarHash` is set — an uploaded image always wins. */
  avatarVariant: number | null
  /** Unused recovery codes. Zero means this account has no way back in if the
   *  password is lost, which is what the post-login prompt exists to fix. */
  recoveryCodesRemaining: number
  /** Two-factor sign-in (dev-plan 3.5). */
  totpEnabled: boolean
  /** An administrator who must enrol before administering. */
  totpRequired: boolean
}

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

export type Space = {
  id: string
  key: string
  name: string
  description: string | null
  archived: boolean
  homepageId: string | null
  createdAt: string
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
  createdAt: string
  updatedAt: string
}

export type PageTreeNode = {
  id: string
  title: string
  position: number
  children: PageTreeNode[]
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

export type Group = { id: string; name: string; description: string | null; memberCount: number }
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
  /** The password itself is never returned — only whether one is stored. */
  smtpPasswordSet: boolean
  smtpFromAddress: string | null
  smtpTls: number
  requireTotpForAdmins: boolean
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
  status: UserStatus
  avatarHash: string | null
  avatarVariant: number | null
  hasPassword: boolean
  isSso: boolean
  recoveryCodesRemaining: number
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
export type LabelledPage = { pageId: string; spaceId: string; spaceKey: string; title: string }

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
}

/** An error carrying the HTTP status and any field validation messages. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>
  /** A machine-readable reason, e.g. `reauth_required`. */
  readonly code: string | null

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}, code: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
    this.code = code
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

async function handle<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = text ? safeJson(text) : undefined

  if (!res.ok) {
    // ASP.NET Core ValidationProblem shape: { errors: { field: [msgs] } }.
    const fieldErrors = (data && typeof data === 'object' && 'errors' in data
      ? (data as { errors: Record<string, string[]> }).errors
      : {}) as Record<string, string[]>
    const message =
      (data && typeof data === 'object' && 'message' in data
        ? String((data as { message: unknown }).message)
        : undefined) ??
      firstFieldError(fieldErrors) ??
      defaultMessage(res.status)
    const code = data && typeof data === 'object' && 'code' in data
      ? String((data as { code: unknown }).code)
      : null
    throw new ApiError(res.status, message, fieldErrors, code)
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
    /** Returns the user plus the recovery codes — the one moment they exist. */
    register: (email: string, displayName: string, password: string, inviteToken?: string) =>
      request<User & { recoveryCodes: string[] }>('POST', '/api/auth/register', {
        email,
        displayName,
        password,
        inviteToken,
      }),
    logout: () => request<void>('POST', '/api/auth/logout'),
    oidcStatus: () => request<{ enabled: boolean; displayName: string }>('GET', '/api/auth/oidc/status'),
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
  spaces: {
    list: (includeArchived = false) =>
      request<Space[]>('GET', `/api/spaces?includeArchived=${includeArchived}`),
    get: (key: string) => request<Space>('GET', `/api/spaces/${encodeURIComponent(key)}`),
    create: (input: { key: string; name: string; description?: string | null }) =>
      request<Space>('POST', '/api/spaces', input),
    update: (key: string, input: { name: string; description?: string | null }) =>
      request<Space>('PUT', `/api/spaces/${encodeURIComponent(key)}`, input),
    archive: (key: string) =>
      request<Space>('POST', `/api/spaces/${encodeURIComponent(key)}/archive`, {}),
    unarchive: (key: string) =>
      request<Space>('POST', `/api/spaces/${encodeURIComponent(key)}/unarchive`, {}),
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
    update: (id: string, input: { title?: string | null; contentJson: string; changeComment?: string | null }) =>
      request<PageDetail>('PUT', `/api/pages/${id}`, input),
    move: (id: string, input: { parentPageId?: string | null; index: number }) =>
      request<void>('PUT', `/api/pages/${id}/move`, input),
    setLayout: (id: string, input: { fullWidth: boolean }) =>
      request<void>('PUT', `/api/pages/${id}/layout`, input),
    remove: (id: string) => request<void>('DELETE', `/api/pages/${id}`),
    createDraft: (input: { spaceId: string; parentPageId?: string | null }) =>
      request<{ id: string }>('POST', '/api/pages/draft', input),
    publish: (id: string, input: { title: string; contentJson: string }) =>
      request<PageDetail>('POST', `/api/pages/${id}/publish`, input),
    deleteDraft: (id: string) => request<void>('DELETE', `/api/pages/${id}/draft`),
    trash: (spaceId: string) => request<TrashedPage[]>('GET', `/api/pages/trash?spaceId=${spaceId}`),
    untrash: (id: string) => request<void>('POST', `/api/pages/${id}/restore`),
    purge: (id: string) => request<void>('DELETE', `/api/pages/${id}/purge`),
    /** Short-lived token authorising this user to join the page's live session. */
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
      setRole: (id: string, role: UserRole) =>
        request<AdminUser>('PUT', `/api/admin/users/${id}/role`, { role }),
      setStatus: (id: string, status: UserStatus) =>
        request<AdminUser>('PUT', `/api/admin/users/${id}/status`, { status }),
      revokeSessions: (id: string) =>
        request<void>('POST', `/api/admin/users/${id}/revoke-sessions`),
      revokeTokens: (id: string) =>
        request<void>('POST', `/api/admin/users/${id}/revoke-tokens`),
      unlock: (id: string) => request<void>('POST', `/api/admin/users/${id}/unlock`),
      issueReset: (id: string) =>
        request<{ token: string; path: string; expiresAt: string }>(
          'POST', `/api/admin/users/${id}/reset-password`),
    },
    spaces: {
      list: () => request<AdminSpace[]>('GET', '/api/admin/spaces'),
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
     *  Not via `request`, which sets a JSON content type — FormData must set
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
    create: (name: string) => request<CreatedApiToken>('POST', '/api/api-tokens', { name }),
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
      request<LabelledPage[]>('GET', `/api/labels/${encodeURIComponent(name)}/pages`),
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
  },
}
