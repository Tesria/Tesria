import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, type AppNotification } from '../api/client'
import { ALERT_KIND_LABEL } from '../routes/admin/alertKinds'
import { usePopoverMotion } from './popoverMotion'
import { ExportJobView } from './ExportJobView'
import { isActive } from './exportJobLabel'
import { useExportJobs } from './exportJobs'

/** Same stroke-icon language as the editor toolbar (editor/icons.tsx) (flat,
 *  currentColor, 1.8px stroke) instead of the platform's own emoji bell,
 *  which rendered in full color (yellow, browser/OS-drawn) and stood out
 *  against the rest of the app's otherwise flat, monochrome icon set. */
function BellIcon() {
  return (
    <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M6 10a6 6 0 1 1 12 0c0 4 1.5 5.5 2 6.5H4c.5-1 2-2.5 2-6.5Z" />
      <path d="M10 19.5a2 2 0 0 0 4 0" />
    </svg>
  )
}

const ACTION_LABEL: Record<string, string> = {
  'page.created': 'created a page',
  'page.updated': 'updated a page',
  'comment.created': 'commented on a page',
  'user.mentioned': 'mentioned you on a page',
  'page.restrictions_lifted': 'lifted the restrictions on your page',
}

function describe(n: AppNotification): string {
  // Security alerts (dev-plan 3.3) come from the system, not a person.
  if (n.action === 'security.alert') {
    try {
      const meta = JSON.parse(n.metadataJson ?? '{}') as { Kind?: string; Severity?: string }
      const kind = meta.Kind ? ALERT_KIND_LABEL[meta.Kind] ?? meta.Kind : 'see the Security page'
      return `Security ${meta.Severity?.toLowerCase() ?? 'alert'}: ${kind}`
    } catch {
      return 'Security alert'
    }
  }
  // A week's warning before an API token expires (dev-plan 14.1).
  if (n.action === 'token.expiring') {
    try {
      const meta = JSON.parse(n.metadataJson ?? '{}') as { Name?: string; ExpiresAt?: string }
      const when = meta.ExpiresAt
        ? new Date(meta.ExpiresAt).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' })
        : 'soon'
      return `Your API token “${meta.Name ?? ''}” expires on ${when}`
    } catch {
      return 'One of your API tokens expires soon'
    }
  }
  // A space export prepared in the background (dev-plan 20.2).
  if (n.action === 'export.ready' || n.action === 'export.failed') {
    try {
      const meta = JSON.parse(n.metadataJson ?? '{}') as { Title?: string; Format?: string }
      const what = `${meta.Format === 'pack' ? 'pack' : 'site'} of ${meta.Title ?? 'a space'}`
      return n.action === 'export.ready' ? `Your ${what} is ready to download` : `Your ${what} could not be prepared`
    } catch {
      return n.action === 'export.ready' ? 'An export is ready to download' : 'An export could not be prepared'
    }
  }
  // An administrator revoked one of your tokens (the admin API tokens tab).
  if (n.action === 'token.revoked') {
    try {
      const meta = JSON.parse(n.metadataJson ?? '{}') as { Name?: string }
      return `An administrator revoked your API token “${meta.Name ?? ''}”`
    } catch {
      return 'An administrator revoked one of your API tokens'
    }
  }
  const who = n.actorName ?? 'Someone'
  const what = ACTION_LABEL[n.action] ?? n.action
  let title = ''
  if (n.metadataJson) {
    try {
      const meta = JSON.parse(n.metadataJson) as { Title?: string; Body?: string }
      // Older comment notifications stored mentions as tokens; show "@Name".
      const body = meta.Body?.replace(/@\[([^\]\n]{1,200})\]\(user:[0-9a-fA-F-]{36}\)/g, '@$1')
      title = meta.Title ? `: "${meta.Title}"` : body ? `: "${body}"` : ''
    } catch {
      /* ignore malformed metadata */
    }
  }
  return `${who} ${what}${title}`
}

/** Bell icon with unread count; opens a dropdown of recent notifications. */
export function NotificationBell() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const panel = usePopoverMotion<HTMLDivElement>(open)
  const [count, setCount] = useState(0)
  const [items, setItems] = useState<AppNotification[] | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  // Exports being prepared, or ready, shown above the notifications. The
  // bell turns while one is still going.
  const { jobs } = useExportJobs()
  const preparing = jobs.some(isActive)

  function refreshCount() {
    api.notifications.unreadCount().then((r) => setCount(r.count)).catch(() => {})
  }

  useEffect(() => {
    refreshCount()
    const interval = setInterval(refreshCount, 30_000)
    return () => clearInterval(interval)
  }, [])

  useEffect(() => {
    function onClickAway(e: MouseEvent) {
      if (open && rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClickAway)
    return () => document.removeEventListener('mousedown', onClickAway)
  }, [open])

  function toggle() {
    setOpen((v) => {
      const next = !v
      if (next) api.notifications.list().then(setItems).catch(() => {})
      return next
    })
  }

  async function markAllRead() {
    await api.notifications.markAllRead()
    setItems((prev) => prev?.map((n) => ({ ...n, readAt: n.readAt ?? new Date().toISOString() })) ?? null)
    setCount(0)
  }

  async function openNotification(n: AppNotification) {
    if (!n.readAt) {
      api.notifications.markRead(n.id).catch(() => {})
      setCount((c) => Math.max(0, c - 1))
      setItems((prev) => prev?.map((m) => (m.id === n.id ? { ...m, readAt: new Date().toISOString() } : m)) ?? null)
    }
    // An export's file is in Downloads, just above: the panel stays open.
    if (n.targetType === 'export') return
    setOpen(false)
    if (n.targetType === 'token') {
      navigate('/profile#api-tokens')
      return
    }
    if (n.targetType === 'security') {
      navigate('/admin/security')
      return
    }
    try {
      const spaces = await api.spaces.list()
      if (n.targetType === 'page') {
        const page = await api.pages.get(n.targetId)
        const space = spaces.find((s) => s.id === page.spaceId)
        if (space) navigate(`/spaces/${space.key}/pages/${n.targetId}`)
      } else {
        const space = spaces.find((s) => s.id === n.targetId)
        if (space) navigate(`/spaces/${space.key}`)
      }
    } catch {
      /* the target may no longer exist or be reachable: stay put */
    }
  }

  return (
    <div className="notif" ref={rootRef}>
      <button
        type="button"
        className={`notif__bell${open ? ' is-open' : ''}${preparing ? ' is-preparing' : ''}`}
        onClick={toggle}
        aria-label={preparing ? 'Notifications (an export is being prepared)' : 'Notifications'}
        aria-expanded={open}
      >
        <BellIcon />
        {count > 0 && <span className="notif__badge">{count > 99 ? '99+' : count}</span>}
      </button>
      {panel.shown && (
        <div className="notif__dropdown" ref={panel.ref}>
          <div className="notif__header">
            <span className="notif__title">Notifications</span>
            <span className="row-gap" style={{ alignItems: 'center' }}>
              <button type="button" className="link-btn" onClick={markAllRead}>Mark All Read</button>
              <button type="button" className="popover__close" aria-label="Close" onClick={() => setOpen(false)}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </button>
            </span>
          </div>
          {jobs.length > 0 && (
            <section className="notif__downloads" aria-label="Downloads">
              <h3 className="notif__section-title">Downloads</h3>
              <ul className="notif__jobs">
                {jobs.map((job) => (
                  <li key={job.id}><ExportJobView job={job} compact /></li>
                ))}
              </ul>
              <h3 className="notif__section-title">Notifications</h3>
            </section>
          )}
          {items === null && <p className="muted small" style={{ padding: '0.5rem' }}>Loading…</p>}
          {items?.length === 0 && <p className="muted small" style={{ padding: '0.5rem' }}>You're all caught up.</p>}
          <ul className="notif__list">
            {items?.map((n) => (
              <li key={n.id}>
                <button
                  type="button"
                  className={n.readAt ? 'notif__item' : 'notif__item notif__item--unread'}
                  onClick={() => openNotification(n)}
                >
                  <span>{describe(n)}</span>
                  <span className="muted small">{new Date(n.createdAt).toLocaleString()}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
