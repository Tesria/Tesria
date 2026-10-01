// One export in the Downloads list or under its button (dev-plan 20.2): what
// it is, how far along, and what can be done with it.
import { useState } from 'react'
import { api, ApiError, type ExportJob } from '../api/client'
import { isActive, jobLabel } from './exportJobLabel'
import { cancelExport, removeExport } from './exportJobs'

export function ExportJobView({ job, compact = false }: { job: ExportJob; compact?: boolean }) {
  const label = jobLabel(job)
  const [error, setError] = useState<string | null>(null)
  const act = (work: () => Promise<void>) => {
    setError(null)
    work().catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'That did not work.'))
  }
  const percent = label.fraction == null ? null : Math.round(label.fraction * 100)

  return (
    <div className={`export-job export-job--${job.status}${compact ? ' export-job--compact' : ''}`}>
      <div className="export-job__head">
        <strong className="export-job__title">{label.title}</strong>
        <span className="export-job__status small">{label.status}</span>
      </div>
      {label.fraction !== undefined && (
        <div
          className={`export-progress__bar${percent === null ? ' is-indeterminate' : ''}`}
          role="progressbar"
          aria-label={`${label.title}: ${label.status}`}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent ?? undefined}
        >
          <span style={percent === null ? undefined : { width: `${percent}%` }} />
        </div>
      )}
      {(label.detail || error) && (
        <span className={`small export-job__detail${error || job.status === 'failed' ? ' is-error' : ' muted'}`}>
          {error ?? label.detail}
        </span>
      )}
      <div className="export-job__actions">
        {job.status === 'ready' && (
          <a className="btn btn--primary btn--sm" href={api.exports.fileUrl(job.id)} download={job.fileName ?? undefined}>
            Download
          </a>
        )}
        {isActive(job) ? (
          <button type="button" className="btn btn--sm" onClick={() => act(() => cancelExport(job.id))}>Cancel</button>
        ) : (
          <button type="button" className="btn btn--ghost btn--sm" onClick={() => act(() => removeExport(job.id))}>
            Remove
          </button>
        )}
      </div>
    </div>
  )
}
