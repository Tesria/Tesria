// Typed client for the ConfluenceClone REST API. All calls are same-origin and
// send the auth cookie automatically (credentials: 'include' for dev CORS).

export type User = { id: string; email: string; displayName: string }

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
export type Directory = { id: string; email: string; displayName: string }

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
  targetType: 'page' | 'space'
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
  isInline: boolean
  isDeleted: boolean
  createdAt: string
  updatedAt: string
}

/** An error carrying the HTTP status and any field validation messages. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

type Body = object | undefined

async function request<T>(method: string, path: string, body?: Body): Promise<T> {
  const res = await fetch(path, {
    method,
    credentials: 'include',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  return handle<T>(res)
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
    throw new ApiError(res.status, message, fieldErrors)
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
      request<User>('POST', '/api/auth/login', { email, password }),
    register: (email: string, displayName: string, password: string) =>
      request<User>('POST', '/api/auth/register', { email, displayName, password }),
    logout: () => request<void>('POST', '/api/auth/logout'),
    oidcStatus: () => request<{ enabled: boolean; displayName: string }>('GET', '/api/auth/oidc/status'),
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
    move: (id: string, input: { parentPageId?: string | null; position: number }) =>
      request<void>('PUT', `/api/pages/${id}/move`, input),
    remove: (id: string) => request<void>('DELETE', `/api/pages/${id}`),
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
