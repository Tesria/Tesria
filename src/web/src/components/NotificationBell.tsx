import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, type AppNotification } from '../api/client'

const ACTION_LABEL: Record<string, string> = {
  'page.created': 'created a page',
  'page.updated': 'updated a page',
  'comment.created': 'commented on a page',
}

function describe(n: AppNotification): string {
  const who = n.actorName ?? 'Someone'
  const what = ACTION_LABEL[n.action] ?? n.action
  let title = ''
  if (n.metadataJson) {
    try {
      const meta = JSON.parse(n.metadataJson) as { Title?: string; Body?: string }
      title = meta.Title ? `: "${meta.Title}"` : meta.Body ? `: "${meta.Body}"` : ''
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
      /* the target may no longer exist or be reachable — stay put */
    }
  }

  return (
    <div className="notif" ref={rootRef}>
      <button type="button" className="notif__bell" onClick={toggle} aria-label="Notifications">
        🔔
        {count > 0 && <span className="notif__badge">{count > 99 ? '99+' : count}</span>}
      </button>
      {open && (
        <div className="notif__dropdown">
          <div className="notif__header">
            <span>Notifications</span>
            <button type="button" className="link-btn" onClick={markAllRead}>Mark all read</button>
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
