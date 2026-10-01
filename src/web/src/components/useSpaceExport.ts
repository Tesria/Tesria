// Starting a space's site or pack export from Space Settings (dev-plan 20.2),
// and the latest one of them, wherever it was started.
import { useState } from 'react'
import { ApiError } from '../api/client'
import { isActive } from './exportJobLabel'
import { startExport, useExportJobs } from './exportJobs'

export function useSpaceExport(spaceKey: string, format: 'site' | 'pack') {
  const { jobs } = useExportJobs()
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const latest = jobs.find((j) => j.format === format && j.spaceKey.toUpperCase() === spaceKey.toUpperCase()) ?? null

  async function start(options: { audience?: 'anonymous' | 'me' } = {}) {
    setStarting(true)
    setError(null)
    try {
      await startExport({
        spaceKey,
        format,
        ...options,
        // A site opens in the look the person exporting it is using (0.8.1).
        style: document.documentElement.getAttribute('data-style') === 'glass' ? 'glass' : null,
      })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'It could not be started.')
    } finally {
      setStarting(false)
    }
  }

  return { latest, preparing: starting || (latest !== null && isActive(latest)), error, start }
}
