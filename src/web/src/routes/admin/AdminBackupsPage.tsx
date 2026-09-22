import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { StorageTargets } from './StorageTargets'
import { KeptCopyCard, RestoreDialog, RestoreProgress } from './RestorePanel'
import { DiskSpace } from './DiskSpace'
import {
  api,
  ApiError,
  type Backup,
  type BackupAgent,
  type BackupAgentName,
  type BackupJob,
  type BackupOverview,
  type BackupPolicyInput,
  type BackupPreview,
} from '../../api/client'
import { bytes, duration, relative } from './format'
import { useAuth } from '../../auth/AuthContext'

const AGENT_TITLE: Record<BackupAgentName, string> = {
  logical: 'Database dumps and uploads',
  physical: 'Physical backups and point-in-time recovery',
}

const AGENT_SHORT: Record<BackupAgentName, string> = {
  logical: 'Dump',
  physical: 'Physical',
}

const POLL_MS = 5000

const JOB_KIND: Record<string, string> = {
  backup: 'backup',
  'restore-test': 'restore test',
  'copy-offsite': 'copy to a drive',
  restore: 'RESTORE',
  'restore-undo': 'undo of a restore',
  'restore-discard': 'removal of the kept copy',
}

function when(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : 'Unknown'
}

function parse(json: string | null): Record<string, unknown> {
  try {
    return json ? (JSON.parse(json) as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

/**
 * One line saying what a run did, from the result the sidecar wrote. The
 * full output is in the expandable log.
 */
function summary(job: BackupJob): string {
  if (job.status === 'requested') return 'Waiting for the backup agent to pick this up (within a minute).'
  if (job.status === 'running') return 'Running…'
  const r = parse(job.resultJson)
  if (job.kind === 'restore-test') {
    if (job.status === 'failed') return job.error ?? 'The restore failed.'
    const tables = typeof r.tablesRestored === 'number' ? `, ${r.tablesRestored} tables` : ''
    return `Restored ${job.target ?? 'the newest backup'} cleanly${tables}.`
  }
  if (job.status === 'failed') return job.error ?? 'The backup failed.'
  const parts: string[] = []
  const produced = Array.isArray(r.produced) ? (r.produced as { label: string; type: string; sizeBytes: number }[]) : []
  if (produced.length > 0) parts.push(produced.map((p) => `${p.type} ${bytes(p.sizeBytes)}`).join(', '))
  const removed = Array.isArray(r.removed) ? r.removed.length : 0
  if (removed > 0) parts.push(`retention removed ${removed}`)
  const retention = (r.retention ?? {}) as { pendingUntil?: string | null }
  if (retention.pendingUntil) parts.push(`new policy waits until ${new Date(retention.pendingUntil).toLocaleString()}`)
  if (typeof r.orphansRemoved === 'number' && r.orphansRemoved > 0) parts.push(`${r.orphansRemoved} temp file(s) cleaned`)
  return parts.length > 0 ? parts.join(' · ') : 'Done.'
}

type Tone = 'ok' | 'warn' | 'bad' | 'idle'

/** Danger is for failure only. A fresh instance with no backup yet is not a failure. */
function agentState(a: BackupAgent): { tone: Tone; text: string } {
  if (!a.reporting) return { tone: 'idle', text: 'Not reporting yet' }
  if (a.lastRunFailed) return { tone: 'bad', text: 'Last run failed' }
  if (a.overdue) return { tone: 'bad', text: 'Overdue' }
  if (!a.online) return { tone: 'warn', text: 'Agent offline' }
  if (a.diskLow) return { tone: 'warn', text: 'Disk nearly full' }
  if (!a.lastSuccess) return { tone: 'idle', text: 'No backup yet' }
  return { tone: 'ok', text: 'Healthy' }
}

function Dot({ tone }: { tone: Tone }) {
  return <span className={`backup-dot backup-dot--${tone}`} aria-hidden="true" />
}

function StatusBadge({ status }: { status: BackupJob['status'] }) {
  const cls = status === 'failed' ? 'badge badge--danger' : status === 'succeeded' ? 'badge badge--ok' : 'badge badge--warn'
  return <span className={cls}>{status}</span>
}

function describeBackup(b: Backup): string {
  if (b.agent === 'logical') return b.hasUploads ? 'Dump + uploads' : 'Dump only'
  if (b.type === 'full') return 'Physical full'
  return `Physical ${b.type}`
}

function AgentCard({
  agent, busy, pendingText, onTest, canRun,
}: {
  agent: BackupAgent
  busy: boolean
  pendingText: string | null
  onTest: (label: string) => void
  canRun: boolean
}) {
  const state = agentState(agent)
  const newest = agent.lastSuccess
  return (
    <section className="backup-card">
      <h2 className="backup-card__title">{AGENT_TITLE[agent.name]}</h2>
      <p className="backup-card__state"><Dot tone={state.tone} /> {state.text}</p>
      <dl className="backup-card__facts">
        <dt>Last backup</dt>
        <dd>
          {newest
            ? <>{relative(newest.completedAt ?? newest.startedAt)} · {bytes(newest.sizeBytes)}</>
            : 'None yet'}
        </dd>
        <dt>Next run</dt>
        <dd>{agent.nextRunAt ? `${relative(agent.nextRunAt)} (${when(agent.nextRunAt)})` : 'Not scheduled'}</dd>
        {agent.name === 'physical' ? (
          <>
            <dt>Restore to any moment</dt>
            <dd>
              {agent.oldestRestorePoint
                ? <>{when(agent.oldestRestorePoint)} → {agent.walArchivedAt ? when(agent.walArchivedAt) : 'now'}</>
                : 'No full backup yet'}
            </dd>
          </>
        ) : (
          <>
            <dt>Oldest restore point</dt>
            <dd>{when(agent.oldestRestorePoint)}</dd>
          </>
        )}
        <dt>Kept</dt>
        <dd>{agent.presentCount} backup{agent.presentCount === 1 ? '' : 's'} · {bytes(agent.presentBytes)}</dd>
        <dt>Disk free</dt>
        <dd>
          {agent.volumeFreeBytes != null && agent.volumeTotalBytes
            ? `${bytes(agent.volumeFreeBytes)} of ${bytes(agent.volumeTotalBytes)}`
            : 'Not reported'}
        </dd>
        <dt>Last restore test</dt>
        <dd>
          {agent.lastVerifiedAt
            ? <>{relative(agent.lastVerifiedAt)} · {agent.lastVerifyOk ? 'passed' : <span className="backup-text--bad">failed</span>}</>
            : 'Never'}
        </dd>
        <dt>Success (30 days)</dt>
        <dd>{agent.successRate30Days == null ? 'No runs yet' : `${Math.round(agent.successRate30Days * 100)}%`}</dd>
      </dl>
      {agent.lastRunFailed && agent.lastFailure?.error && (
        <p className="alert alert--error small">{agent.lastFailure.error}</p>
      )}
      {pendingText && <p className="backup-card__pending small">{pendingText}</p>}
      <p className="muted small">
        {agent.intervalHours ? `Every ${agent.intervalHours} h` : ''}
        {agent.fullEveryDays ? ` · a full backup every ${agent.fullEveryDays} days` : ''}
        {agent.toolVersion ? ` · ${agent.toolVersion}` : ''}
        {agent.lastSeenAt ? ` · checked in ${relative(agent.lastSeenAt)}` : ''}
      </p>
      <button
        type="button"
        className="btn btn--ghost btn--sm"
        disabled={busy || !newest || !agent.reporting || !canRun}
        onClick={() => newest && onTest(newest.label)}
      >
        Test restore of newest
      </button>
    </section>
  )
}

/** Admin → Backups (dev-plan 9.1). */
export function AdminBackupsPage() {
  const { can } = useAuth()
  const [data, setData] = useState<BackupOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [showRemoved, setShowRemoved] = useState(false)
  // The backup whose Restore dialog is open (dev-plan 9.4).
  const [restoringBackup, setRestoring] = useState<Backup | null>(null)
  const [draft, setDraft] = useState<BackupPolicyInput | null>(null)
  const [preview, setPreview] = useState<{ input: BackupPolicyInput; result: BackupPreview } | null>(null)
  const [openLog, setOpenLog] = useState<string | null>(null)
  const [logs, setLogs] = useState<Record<string, BackupJob>>({})

  const load = useCallback(() => {
    api.admin.backups
      .overview(showRemoved)
      .then((d) => {
        setData(d)
        setDraft((current) => current ?? { enabled: d.policy.enabled, keepCount: d.policy.keepCount, keepDays: d.policy.keepDays })
      })
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'Could not load backups.'))
  }, [showRemoved])

  useEffect(load, [load])

  // While anything is queued or running, follow it until it finishes, then
  // reload the whole page's data once.
  const active = useMemo(
    () => (data?.jobs ?? []).filter((j) => j.status === 'requested' || j.status === 'running').map((j) => j.id),
    [data],
  )
  useEffect(() => {
    if (active.length === 0) return
    const timer = window.setInterval(() => {
      Promise.all(active.map((id) => api.admin.backups.job(id)))
        .then((jobs) => {
          if (jobs.some((j) => j.status === 'succeeded' || j.status === 'failed')) load()
          else setData((d) => d && { ...d, jobs: d.jobs.map((j) => jobs.find((x) => x.id === j.id) ?? j) })
        })
        .catch(() => undefined)
    }, POLL_MS)
    return () => window.clearInterval(timer)
  }, [active, load])

  async function run<T>(work: () => Promise<T>, done: string, failure: string): Promise<T | null> {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      const result = await work()
      setStatus(done)
      return result
    } catch (err) {
      setError(err instanceof ApiError ? err.message : failure)
      return null
    } finally {
      setBusy(false)
    }
  }

  async function backUpNow() {
    await run(() => api.admin.backups.run(), 'Backups queued. Each agent picks its job up within a minute.', 'Could not queue the backups.')
    load()
  }

  async function testRestore(label: string) {
    await run(() => api.admin.backups.restoreTest(label), `Restore test of ${label} queued.`, 'Could not queue the restore test.')
    load()
  }

  // Replacing the wiki with an older copy (dev-plan 9.4). The dialog does the
  // confirming; this only opens it and picks the page up afterwards.
  async function cancelRestore() {
    const result = await run(
      () => api.admin.backups.cancelRestore(), '', 'Could not stop the restore.')
    if (result) setStatus(result.message)
    load()
  }

  async function review(e: FormEvent) {
    e.preventDefault()
    if (!draft) return
    const result = await run(() => api.admin.backups.preview(draft), '', 'Could not preview the policy.')
    setStatus(null)
    if (result) setPreview({ input: draft, result })
  }

  async function confirmPolicy() {
    if (!preview) return
    const saved = await run(() => api.admin.backups.savePolicy(preview.input), 'Retention policy saved.', 'Could not save the policy.')
    if (saved) {
      setPreview(null)
      setDraft({ enabled: saved.enabled, keepCount: saved.keepCount, keepDays: saved.keepDays })
      load()
    }
  }

  async function toggleLog(id: string) {
    if (openLog === id) {
      setOpenLog(null)
      return
    }
    setOpenLog(id)
    try {
      const job = await api.admin.backups.job(id)
      setLogs((l) => ({ ...l, [id]: job }))
    } catch {
      /* the row still shows its summary */
    }
  }

  if (!data || !draft) return <p className="muted">{error ?? 'Loading…'}</p>

  const mayRestore = can('backups.restore')
  const restoring = data.restore.jobId !== null

  const policy = data.policy
  const mayEditPolicy = can('backups.policy')
  const dirty = draft.enabled !== policy.enabled || draft.keepCount !== policy.keepCount || draft.keepDays !== policy.keepDays

  function pendingText(agent: BackupAgent): string | null {
    if (!agent.policyPending) return null
    return agent.policyEffectiveAt
      ? `The new retention policy takes effect ${relative(agent.policyEffectiveAt)} (${when(agent.policyEffectiveAt)}). Nothing is removed before then.`
      : 'The agent has not seen the new retention policy yet. It waits 24 hours from when it does; nothing is removed before then.'
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      {restoring && <RestoreProgress restore={data.restore} onCancel={cancelRestore} />}

      <div className="backup-actions">
        {can('backups.run') && (
          <button type="button" className="btn btn--primary" disabled={busy || active.length > 0} onClick={backUpNow}>
            Back up now
          </button>
        )}
        <span className="muted small">
          {active.length > 0
            ? `${active.length} job${active.length === 1 ? '' : 's'} in progress; this page updates when they finish.`
            : 'Takes a database dump, an uploads archive and a physical backup.'}
        </span>
      </div>

      <div className="backup-cards">
        {data.agents.map((a) => (
          <AgentCard key={a.name} agent={a} busy={busy} pendingText={pendingText(a)} onTest={testRestore}
            canRun={can('backups.run')} />
        ))}
      </div>

      <DiskSpace disks={data.disks} />

      <StorageTargets
        targets={data.targets}
        manualOnly={data.offsiteIsManualOnly}
        canRun={can('backups.run')}
        onCopied={load}
      />

      <section className="profile__section profile__section--wide">
        <h2>Retention policy</h2>
        <p className="muted small">
          Applies to both kinds of backup, and to nothing else. The backup agents apply it after each successful
          backup. A policy that could remove more waits 24 hours before it takes effect, and every administrator
          is alerted.
        </p>
        <form onSubmit={review} className="backup-policy">
          <label className="admin__toggle">
            <input type="radio" name="mode" checked={!draft.enabled} disabled={!mayEditPolicy}
              onChange={() => setDraft({ ...draft, enabled: false })} />
            <span><strong>Keep every backup forever</strong><br />
              <span className="muted small">Nothing is ever removed. Watch the disk space above.</span></span>
          </label>
          <label className="admin__toggle">
            <input type="radio" name="mode" checked={draft.enabled} disabled={!mayEditPolicy}
              onChange={() => setDraft({ ...draft, enabled: true })} />
            <span><strong>Prune old backups</strong></span>
          </label>
          <p className="backup-policy__rule">
            Keep the newest{' '}
            <input type="number" min={1} max={1000} value={draft.keepCount} disabled={!draft.enabled || !mayEditPolicy}
              aria-label="Backups to keep"
              onChange={(e) => setDraft({ ...draft, keepCount: Number(e.target.value) })} />{' '}
            backups and everything from the last{' '}
            <input type="number" min={1} max={3650} value={draft.keepDays} disabled={!draft.enabled || !mayEditPolicy}
              aria-label="Days to keep"
              onChange={(e) => setDraft({ ...draft, keepDays: Number(e.target.value) })} />{' '}
            days.
          </p>
          <p className="muted small">A backup is deleted only when it is outside both.</p>
          <button type="submit" className="btn btn--primary" disabled={busy || !dirty || !mayEditPolicy}>
            Review change
          </button>
          {!mayEditPolicy && (
            <p className="muted small">Your role does not allow changing the retention policy.</p>
          )}
          {policy.changedAt && (
            <p className="muted small">
              Last changed {when(policy.changedAt)}{policy.changedByName ? ` by ${policy.changedByName}` : ' (set from BACKUP_RETENTION_DAYS on upgrade)'}.
            </p>
          )}
        </form>

        {preview && (
          <div className="backup-preview" role="dialog" aria-label="Confirm the retention policy">
            <h3>
              {preview.input.enabled
                ? `Keep the newest ${preview.input.keepCount} and everything from the last ${preview.input.keepDays} days`
                : 'Keep every backup forever'}
            </h3>
            {preview.result.agents.map((a) => (
              <div key={a.agent} className="backup-preview__agent">
                <p><strong>{AGENT_TITLE[a.agent]}</strong></p>
                {a.removed.length === 0 ? (
                  <p className="muted small">Nothing would be removed today.</p>
                ) : (
                  <>
                    <p className="small">
                      {a.removed.length} backup{a.removed.length === 1 ? '' : 's'} ({bytes(a.removedBytes)}) would be removed:
                    </p>
                    <ul className="backup-preview__list small">
                      {a.removed.map((b) => <li key={b.id}><code>{b.label}</code> · {when(b.startedAt)}</li>)}
                    </ul>
                  </>
                )}
                <p className="muted small">
                  Oldest restore point: {when(a.oldestRestorePoint)}
                  {a.removed.length > 0 && <> → <strong>{when(a.newOldestRestorePoint)}</strong></>}
                </p>
              </div>
            ))}
            {preview.result.stricter && (
              <p className="alert alert--error small">
                This policy can remove backups the current one keeps. The agents wait 24 hours before applying it,
                and every administrator is alerted now.
              </p>
            )}
            <div className="backup-preview__actions">
              <button type="button" className="btn btn--primary" disabled={busy} onClick={confirmPolicy}>Save policy</button>
              <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => setPreview(null)}>Cancel</button>
            </div>
          </div>
        )}
      </section>

      <KeptCopyCard restore={data.restore} onChanged={load} />

      <section className="profile__section profile__section--wide">
        <h2>Backups</h2>
        {data.backups.length === 0 ? (
          <p className="muted small">No backups have been recorded yet.</p>
        ) : (
          <table className="admin-table">
            <thead><tr><th>Backup</th><th>Kind</th><th>Taken</th><th>Size</th><th>Verified</th><th></th></tr></thead>
            <tbody>
              {data.backups.map((b) => (
                <tr key={b.id}>
                  <td className="nowrap">
                    <code>{b.label}</code>
                    {b.error && <> <span className="badge badge--danger">error</span></>}
                  </td>
                  <td className="nowrap">
                    {describeBackup(b)}
                    {b.fullLabel && <div className="muted small">of {b.fullLabel}</div>}
                  </td>
                  <td className="nowrap">{when(b.startedAt)}</td>
                  <td className="nowrap">{bytes(b.sizeBytes)}</td>
                  <td className="nowrap">
                    {b.lastVerifiedAt
                      ? <>{b.lastVerifyOk ? 'Passed' : <span className="backup-text--bad">Failed</span>} <span className="muted small">{relative(b.lastVerifiedAt)}</span></>
                      : <span className="muted">Never</span>}
                  </td>
                  <td className="admin-table__actions">
                    <button type="button" className="link-btn" disabled={busy || !!b.error || !can('backups.run')}
                      onClick={() => testRestore(b.label)}>
                      Test restore
                    </button>
                    {mayRestore && (
                      <button type="button" className="link-btn link-btn--danger"
                        disabled={busy || !!b.error || restoring}
                        onClick={() => setRestoring(b)}>
                        Restore
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <label className="admin__toggle admin__toggle--inline backup-removed-toggle">
          <input type="checkbox" checked={showRemoved} onChange={(e) => setShowRemoved(e.target.checked)} />
          <span>Show backups removed in the last 30 days</span>
        </label>
        {showRemoved && (
          data.removed.length === 0 ? (
            <p className="muted small">None.</p>
          ) : (
            <table className="admin-table">
              <thead><tr><th>Backup</th><th>Kind</th><th>Taken</th><th>Removed</th><th>Why</th></tr></thead>
              <tbody>
                {data.removed.map((b) => (
                  <tr key={b.id}>
                    <td className="nowrap"><code>{b.label}</code></td>
                    <td className="nowrap">{describeBackup(b)}</td>
                    <td className="nowrap">{when(b.startedAt)}</td>
                    <td className="nowrap">{when(b.removedAt)}</td>
                    <td>{b.removedReason === 'retention' ? 'Retention policy' : 'Files disappeared without a retention run'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        )}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Recent runs</h2>
        {data.jobs.length === 0 ? (
          <p className="muted small">No runs recorded yet.</p>
        ) : (
          <table className="admin-table">
            <thead><tr><th>When</th><th>What</th><th>Status</th><th>Took</th><th>Result</th><th></th></tr></thead>
            <tbody>
              {data.jobs.map((j) => (
                <JobRows key={j.id} job={j} open={openLog === j.id} detail={logs[j.id]} onToggle={() => toggleLog(j.id)} />
              ))}
            </tbody>
          </table>
        )}
      </section>

      {restoringBackup && (
        <RestoreDialog
          backup={restoringBackup}
          onClose={() => setRestoring(null)}
          onQueued={() => { setRestoring(null); setStatus('The restore has started. The wiki is read-only until it finishes.'); load() }}
        />
      )}
    </>
  )
}

function JobRows({ job, open, detail, onToggle }: { job: BackupJob; open: boolean; detail?: BackupJob; onToggle: () => void }) {
  const what = `${AGENT_SHORT[job.agent]} ${JOB_KIND[job.kind] ?? 'backup'}`
  const by = job.trigger === 'manual' ? `by ${job.requestedByName ?? 'an administrator'}` : job.trigger
  return (
    <>
      <tr>
        <td className="nowrap">{when(job.requestedAt)}</td>
        <td className="nowrap">{what}<div className="muted small">{by}</div></td>
        <td><StatusBadge status={job.status} /></td>
        <td className="nowrap">{duration(job.startedAt, job.finishedAt)}</td>
        <td className="backup-result">{summary(job)}</td>
        <td className="admin-table__actions">
          {(job.status === 'succeeded' || job.status === 'failed') && (
            <button type="button" className="link-btn" onClick={onToggle}>{open ? 'Hide log' : 'Log'}</button>
          )}
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={6}>
            <pre className="backup-log">{detail ? detail.logTail || '(no output)' : 'Loading…'}</pre>
          </td>
        </tr>
      )}
    </>
  )
}
