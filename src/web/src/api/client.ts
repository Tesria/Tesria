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

export type VersionMeta = {
  id: string
  versionNumber: number
  changeComment: string | null
  authorId: string
  createdAt: string
}

export type VersionContent = VersionMeta & { contentJson: string }

export type Attachment = {
  id: string
  pageId: string
  filename: string
  contentType: string
  size: number
  uploadedById: string
  createdAt: string
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
  comments: {
    listForPage: (pageId: string) => request<Comment[]>('GET', `/api/pages/${pageId}/comments`),
    create: (pageId: string, input: { body: string; parentCommentId?: string | null; anchorJson?: string | null }) =>
      request<Comment>('POST', `/api/pages/${pageId}/comments`, input),
    update: (id: string, body: string) => request<Comment>('PUT', `/api/comments/${id}`, { body }),
    remove: (id: string) => request<void>('DELETE', `/api/comments/${id}`),
  },
}
