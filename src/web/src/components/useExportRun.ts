import { useEffect, useRef, useState } from 'react'
import { api, ApiError, type ExportOptions, type ExportProgress } from '../api/client'

/**
 * Progress for Export as a site and Export as a pack (dev-plan 20.1).
 *
 * The export runs in its own request, as before. The page makes up an id,
 * sends it with the request, and asks the server how far along that id is
 * while it waits: pages done out of the total, the page being worked on,
 * and time so far. Once the file is built, the download itself is counted
 * in bytes. Cancel aborts the request, which stops the work on the server.
 */
type Phase = 'idle' | 'running' | 'downloading'

export function useExportRun(fileName: string, failure: string) {
  const [phase, setPhase] = useState<Phase>('idle')
  const [progress, setProgress] = useState<ExportProgress | null>(null)
  const [download, setDownload] = useState<{ received: number; total: number | null } | null>(null)
  const [startedAt, setStartedAt] = useState<number | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [error, setError] = useState<string | null>(null)
  const abort = useRef<AbortController | null>(null)

  // The clock, once a second, while something is running.
  useEffect(() => {
    if (phase === 'idle') return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [phase])

  // A tab closed or navigated away from mid-export stops it.
  useEffect(() => () => abort.current?.abort(), [])

  async function run(start: (options: ExportOptions) => Promise<Blob>) {
    const controller = new AbortController()
    abort.current = controller
    const progressId = crypto.randomUUID()
    setPhase('running')
    setProgress(null)
    setDownload(null)
    setError(null)
    setStartedAt(Date.now())
    setNow(Date.now())

    // Asking while it runs. A 404 only means the export has not started
    // yet (or reports to nobody); either way the bar waits.
    let polling = true
    const poll = async () => {
      while (polling && !controller.signal.aborted) {
        try {
          const seen = await api.spaces.exportProgress(progressId)
          setProgress(seen)
          if (seen.finished) return
        } catch {
          /* not started yet */
        }
        await new Promise((r) => window.setTimeout(r, 800))
      }
    }
    void poll()

    try {
      const zip = await start({
        progressId,
        signal: controller.signal,
        onDownload: (received, total) => {
          setPhase('downloading')
          setDownload({ received, total })
        },
      })
      // A download rather than a navigation: the response is a file, and the
      // page should stay where it is.
      const url = URL.createObjectURL(zip)
      const link = document.createElement('a')
      link.href = url
      link.download = fileName
      link.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      if (controller.signal.aborted) setError(null)
      else setError(err instanceof ApiError ? err.message : failure)
    } finally {
      polling = false
      abort.current = null
      setPhase('idle')
    }
  }

  function cancel() {
    abort.current?.abort()
  }

  const elapsed = startedAt === null ? 0 : Math.max(0, Math.round((now - startedAt) / 1000))
  return { phase, busy: phase !== 'idle', progress, download, elapsed, error, run, cancel }
}

export type ExportRun = ReturnType<typeof useExportRun>
