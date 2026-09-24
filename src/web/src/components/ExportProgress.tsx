// The progress of a site or pack export, as useExportRun tracks it (dev-plan 20.1).
import type { ExportRun } from './useExportRun'

const minutes = (seconds: number) =>
  seconds < 60 ? `${seconds} s` : `${Math.floor(seconds / 60)} min ${String(seconds % 60).padStart(2, '0')} s`

const megabytes = (bytes: number) => `${(bytes / (1024 * 1024)).toFixed(1)} MB`

/** The bar, what it is doing, and Cancel. Nothing while idle. */
export function ExportProgressView({ state, noun }: { state: ExportRun; noun: string }) {
  const { phase, progress, download, elapsed } = state
  if (phase === 'idle') return null

  let label: string
  let fraction: number | null = null
  let detail: string | null = null
  if (phase === 'downloading' && download) {
    label = `Downloading the ${noun}`
    fraction = download.total ? download.received / download.total : null
    detail = download.total
      ? `${megabytes(download.received)} of ${megabytes(download.total)}`
      : megabytes(download.received)
  } else if (progress?.finished) {
    label = `Sending the ${noun}`
  } else if (progress && progress.total > 0) {
    label = `${progress.stage}: ${progress.done} of ${progress.total}`
    fraction = progress.done / progress.total
    detail = progress.current
  } else {
    label = progress?.stage ?? 'Starting'
  }

  const percent = fraction === null ? null : Math.round(Math.min(1, fraction) * 100)
  return (
    <div className="export-progress" aria-live="polite">
      <div className="export-progress__head">
        <strong>{label}</strong>
        <span className="muted small">{minutes(elapsed)}</span>
      </div>
      <div
        className={`export-progress__bar${percent === null ? ' is-indeterminate' : ''}`}
        role="progressbar"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent ?? undefined}
      >
        <span style={percent === null ? undefined : { width: `${percent}%` }} />
      </div>
      <div className="export-progress__foot">
        <span className="muted small export-progress__detail">{detail ?? ' '}</span>
        <button type="button" className="btn btn--sm" onClick={state.cancel}>Cancel</button>
      </div>
    </div>
  )
}
