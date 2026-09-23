import { useEffect, useState } from 'react'
import { WatchIcon } from './NavIcons'

type Props = {
  /** Identifies the watched resource so the status re-fetches when it changes
   *  (the host route doesn't remount on a param-only navigation). */
  watchKey: string
  fetchStatus: () => Promise<{ watching: boolean }>
  watch: () => Promise<void>
  unwatch: () => Promise<void>
  label?: string
}

/** A watch/unwatch button backed by whichever page or space endpoints are passed in. */
export function WatchToggle({ watchKey, fetchStatus, watch, unwatch, label = 'page' }: Props) {
  const [watching, setWatching] = useState<boolean | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let canceled = false
    setWatching(null)
    fetchStatus().then((s) => !canceled && setWatching(s.watching)).catch(() => {})
    return () => {
      canceled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [watchKey])

  async function toggle() {
    setBusy(true)
    try {
      if (watching) {
        await unwatch()
        setWatching(false)
      } else {
        await watch()
        setWatching(true)
      }
    } finally {
      setBusy(false)
    }
  }

  if (watching === null) return null

  return (
    <button type="button" className="btn btn--ghost" onClick={toggle} disabled={busy}>
      <WatchIcon />
      {watching ? 'Watching' : `Watch this ${label}`}
    </button>
  )
}
