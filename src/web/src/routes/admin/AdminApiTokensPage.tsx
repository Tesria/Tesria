import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  api, ApiError, Permission,
  type AdminApiToken, type AdminTokenSummary, type AssistantActivity, type TokenUsageCounts,
} from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/ConfirmDialog'
import { relative } from './format'
import { Sparkline } from './Sparkline'

/**
 * Administration → API tokens (the owner, 2026-09-24): every token on the
 * instance, whose it is, how much it is used through the REST API and by
 * assistants through MCP, and what those assistants did, with Revoke for one
 * token at a time.
 *
 * Seeing needs "See the user list"; revoking, "Manage users", and the server
 * still refuses the owner's tokens, and another administrator's without the
 * right the owner gives. A page an assistant touched is named only if you may
 * read it yourself.
 */

/** What each MCP tool does, in the words the list uses. */
const TOOL: Record<string, string> = {
  list_spaces: 'Listed the spaces',
  get_space_tree: 'Read a space’s page tree',
  get_page: 'Read a page',
  search_pages: 'Searched',
  find_pages_by_label: 'Found pages by label',
  list_labels: 'Listed labels',
  create_page: 'Created a page',
  update_page: 'Changed a page',
  add_page_label: 'Added a label',
  remove_page_label: 'Removed a label',
}

const total = (c: TokenUsageCounts) => c.reads + c.writes + c.mcpReads + c.mcpWrites
const changes = (n: number) => `${n} ${n === 1 ? 'change' : 'changes'}`

function Stat({ label, value, hint }: { label: string; value: string | number; hint?: string }) {
  return (
    <div className="stat">
      <p className="stat__label">{label}</p>
      <p className="stat__value">{value}</p>
      {hint && <p className="muted small">{hint}</p>}
    </div>
  )
}

export function AdminApiTokensPage() {
  const { can } = useAuth()
  const canRevoke = can(Permission.UsersManage)
  const { ask, dialog } = useConfirm()
  const [tokens, setTokens] = useState<AdminApiToken[] | null>(null)
  const [summary, setSummary] = useState<AdminTokenSummary | null>(null)
  const [activity, setActivity] = useState<AssistantActivity[] | null>(null)
  const [only, setOnly] = useState<AdminApiToken | null>(null)
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function load() {
    const [list, head] = await Promise.all([api.admin.apiTokens.list(), api.admin.apiTokens.summary()])
    setTokens(list)
    setSummary(head)
  }

  useEffect(() => {
    load().catch(() => setError('Could not load the API tokens.'))
  }, [])

  useEffect(() => {
    api.admin.apiTokens.activity(only?.id)
      .then(setActivity)
      .catch(() => setError('Could not load what assistants did.'))
  }, [only])

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase()
    if (!tokens) return null
    if (!q) return tokens
    return tokens.filter((t) =>
      [t.name, t.prefix, t.owner.displayName, t.owner.email].some((v) => v.toLowerCase().includes(q)))
  }, [tokens, filter])

  async function revoke(t: AdminApiToken) {
    const ok = await ask({
      title: `Revoke “${t.name}”?`,
      danger: true,
      confirmLabel: 'Revoke the token',
      body: (
        <>
          <p>
            It belongs to <strong>{t.owner.displayName}</strong>. Whatever uses it, a script or an
            assistant, stops working at once, and the token cannot be brought back.
          </p>
          <p>Their other tokens keep working. They are told in the bell (and by email, if they get notifications by email).</p>
        </>
      ),
    })
    if (!ok) return
    setError(null)
    try {
      await api.admin.apiTokens.revoke(t.id)
      if (only?.id === t.id) setOnly(null)
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not revoke the token.')
    }
  }

  return (
    <>
      <p className="muted small">
        Every API token on this Tesria: whose it is, how much it is used through the REST API and by
        assistants through MCP, and what those assistants did. A token acts as the person who made it,
        with exactly what they may see and do. The tokens themselves are never shown here.
      </p>
      {error && <p className="alert alert--error">{error}</p>}

      {summary && (
        <>
          <div className="dash__grid">
            <Stat label="Tokens" value={summary.tokens}
              hint={summary.neverUsed ? `${summary.neverUsed} never used` : undefined} />
            <Stat label="In use (7 days)" value={summary.activeLast7Days} />
            <Stat label="API requests (7 days)" value={summary.last7Days.reads + summary.last7Days.writes}
              hint={changes(summary.last7Days.writes)} />
            <Stat label="Assistant tool calls (7 days)" value={summary.last7Days.mcpReads + summary.last7Days.mcpWrites}
              hint={changes(summary.last7Days.mcpWrites)} />
            <Stat label="Expiring within a week" value={summary.expiringWithinWeek} />
          </div>
          <div className="dash__grid">
            <div className="stat stat--wide">
              <p className="stat__label">API requests per day</p>
              <Sparkline points={summary.apiPerDay} label="API requests" />
            </div>
            <div className="stat stat--wide">
              <p className="stat__label">Assistant tool calls per day</p>
              <Sparkline points={summary.mcpPerDay} label="Assistant tool calls" />
            </div>
          </div>
        </>
      )}

      <h2 className="dash__heading">Tokens</h2>
      <label className="admin-tokens__filter">
        <span className="visually-hidden">Filter by person or token</span>
        <input type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter by person or token name" />
      </label>
      {shown && shown.length === 0 && <p className="muted">{tokens?.length ? 'No token matches.' : 'Nobody has made a token yet.'}</p>}
      {shown && shown.length > 0 && (
        <table className="admin-table admin-tokens">
          <thead>
            <tr>
              <th>Person</th>
              <th>Token</th>
              <th>Last used</th>
              <th>Last 7 days</th>
              <th>Expires</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {shown.map((t) => (
              <tr key={t.id}>
                <td>
                  {t.owner.displayName}
                  {t.owner.suspended && <> <span className="badge badge--warning">suspended</span></>}
                  <div className="muted small">{t.owner.email}</div>
                </td>
                <td>
                  {t.name}{' '}
                  {t.readOnly ? <span className="badge">read-only</span> : <span className="badge badge--warning">full access</span>}
                  <div><code className="muted small">{t.prefix}</code></div>
                </td>
                <td className="small">
                  {t.lastUsedAt ? relative(t.lastUsedAt) : <span className="muted">never</span>}
                  {t.lastUsedFrom && <div className="muted">from {t.lastUsedFrom}</div>}
                  <div className="muted">{t.useCount} {t.useCount === 1 ? 'request' : 'requests'} in all</div>
                </td>
                <td className="small">
                  {total(t.last7Days) === 0 ? <span className="muted">none</span> : (
                    <>
                      {t.last7Days.reads + t.last7Days.writes > 0 && (
                        <div>API: {t.last7Days.reads + t.last7Days.writes} ({changes(t.last7Days.writes)})</div>
                      )}
                      {t.last7Days.mcpReads + t.last7Days.mcpWrites > 0 && (
                        <div>Assistant: {t.last7Days.mcpReads + t.last7Days.mcpWrites} ({changes(t.last7Days.mcpWrites)})</div>
                      )}
                    </>
                  )}
                </td>
                <td className="small">
                  {t.expired
                    ? <span className="badge badge--danger">expired</span>
                    : t.expiresAt ? new Date(t.expiresAt).toLocaleDateString() : 'Never'}
                </td>
                <td className="admin-tokens__actions">
                  <button type="button" className="link-btn" onClick={() => setOnly(t)}>Activity</button>
                  {canRevoke && (
                    <button type="button" className="link-btn link-btn--danger" onClick={() => revoke(t)}>Revoke</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2 className="dash__heading" id="assistant-activity">
        {only ? <>What “{only.name}” did through MCP</> : 'What assistants did'}
      </h2>
      <p className="muted small">
        Each tool an assistant called through MCP, newest first, kept for 90 days. What it searched
        for is not recorded. A page you may not open yourself is not named.
        {only && <> <button type="button" className="link-btn" onClick={() => setOnly(null)}>Show every token</button></>}
      </p>
      {activity && activity.length === 0 && <p className="muted">Nothing yet.</p>}
      {activity && activity.length > 0 && (
        <table className="admin-table admin-activity">
          <thead>
            <tr>
              <th>When</th>
              <th>Who</th>
              <th>What</th>
              <th>On</th>
            </tr>
          </thead>
          <tbody>
            {activity.map((a) => (
              <tr key={a.id}>
                <td className="small muted" title={new Date(a.at).toLocaleString()}>{relative(a.at)}</td>
                <td className="small">
                  {a.userName ?? 'Someone'}
                  <div className="muted">{a.tokenName}</div>
                </td>
                <td className="small">
                  {TOOL[a.tool] ?? a.tool}
                  {a.write && <> <span className="badge badge--warning">change</span></>}
                  {!a.ok && <div className="badge badge--danger" title={a.error ?? undefined}>failed</div>}
                </td>
                <td className="small">
                  {a.page
                    ? a.page.hidden || !a.page.title
                      ? <span className="muted">a page you cannot open</span>
                      : <Link to={`/spaces/${a.page.spaceKey}/pages/${a.page.id}`}>{a.page.title}</Link>
                    : a.spaceKey ?? <span className="muted">–</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {dialog}
    </>
  )
}
