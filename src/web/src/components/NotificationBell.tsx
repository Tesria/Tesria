import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, type AppNotification } from '../api/client'
import { ALERT_KIND_LABEL } from '../routes/admin/alertKinds'

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
  const [count, setCount] = useState(0)
  const [items, setItems] = useState<AppNotification[] | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)

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
    setOpen(false)
    if (!n.readAt) {
      api.notifications.markRead(n.id).catch(() => {})
      setCount((c) => Math.max(0, c - 1))
    }
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
      <button type="button" className="notif__bell" onClick={toggle} aria-label="Notifications">
        <BellIcon />
        {count > 0 && <span className="notif__badge">{count > 99 ? '99+' : count}</span>}
      </button>
      {open && (
        <div className="notif__dropdown">
          <div className="notif__header">
            <span>Notifications</span>
            <span className="row-gap" style={{ alignItems: 'center' }}>
              <button type="button" className="link-btn" onClick={markAllRead}>Mark all read</button>
              <button type="button" className="popover__close" aria-label="Close" onClick={() => setOpen(false)}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </button>
            </span>
          </div>
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
