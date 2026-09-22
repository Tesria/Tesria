import { useEffect, useState } from 'react'

type Maintenance = { reason: string; startedAt: string; jobId?: string; describes?: string }

/**
 * The wiki is read-only while a restore runs (dev-plan 9.4).
 *
 * Shown for everybody except the person who started it, who watches the
 * phases on the backups page instead. It appears the moment any write comes
 * back 503, which is the honest trigger: the point is not to predict the
 * restore but to stop somebody believing their save went through.
 *
 * It clears itself by polling the health endpoint, which reports maintenance
 * anonymously. That matters more than it looks: a restore can roll the
 * database back past the session of the person watching, so the poll has to
 * work for a browser that has just been signed out.
 */
export function MaintenanceOverlay() {
  const [state, setState] = useState<Maintenance | null>(null)

  useEffect(() => {
    const onMaintenance = (e: Event) => {
      const detail = (e as CustomEvent).detail as Maintenance | undefined
      if (detail) setState(detail)
    }
    window.addEventListener('tesria:maintenance', onMaintenance)
    return () => window.removeEventListener('tesria:maintenance', onMaintenance)
  }, [])

  useEffect(() => {
    if (!state) return
    let cancelled = false
    const tick = async () => {
      try {
        const res = await fetch('/api/health', { credentials: 'include' })
        const body = (await res.json()) as { maintenance?: Maintenance | null }
        // Over. Reload rather than clear: the wiki behind this overlay is a
        // different database now, and every page in memory is the old one.
        if (!cancelled && !body.maintenance) window.location.reload()
      } catch {
        // The app is restarting into the restored database. Keep polling.
      }
    }
    const timer = window.setInterval(tick, 5000)
    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [state])

  if (!state) return null

  return (
    <div className="maintenance" role="alertdialog" aria-modal="true" aria-labelledby="maintenance-title">
      <div className="maintenance__card">
        <h2 id="maintenance-title">A restore is in progress</h2>
        <p>
          The wiki is read-only while an administrator restores it from a backup. You can keep reading,
          but nothing can be saved until it finishes.
        </p>
        <p className="muted small">
          Started {new Date(state.startedAt).toLocaleString()}. This page returns on its own.
        </p>
        <p className="muted small">
          When it is done you may be asked to sign in again. That is expected: a restore puts back the
          accounts and sessions as they were when the backup was taken.
        </p>
      </div>
    </div>
  )
}
