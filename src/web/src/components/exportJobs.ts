// The person's own space exports (dev-plan 20.2), shared by the Downloads
// list in the notifications panel and the export buttons in Space Settings,
// so a job started on one shows on the other at once. Polled quickly while
// something is being prepared, and slowly otherwise, to notice a job started
// on another device.
import { useSyncExternalStore } from 'react'
import { api, ApiError, type ExportJob } from '../api/client'
import { isActive } from './exportJobLabel'

type State = { jobs: ExportJob[]; loaded: boolean }

const FAST_MS = 1500
const SLOW_MS = 60_000

let state: State = { jobs: [], loaded: false }
const listeners = new Set<() => void>()
let timer: number | null = null
let inflight: Promise<void> | null = null
/** No right to export: nothing to ask for until the next sign-in. */
let refused = false

function set(next: State) {
  state = next
  for (const listener of listeners) listener()
}

function schedule() {
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  if (listeners.size === 0 || refused) return
  timer = window.setTimeout(() => void refreshExportJobs(), state.jobs.some(isActive) ? FAST_MS : SLOW_MS)
}

export function refreshExportJobs(): Promise<void> {
  if (refused) return Promise.resolve()
  inflight ??= api.exports.list()
    .then((jobs) => set({ jobs, loaded: true }))
    .catch((err: unknown) => {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        refused = true
        set({ jobs: [], loaded: true })
      }
    })
    .finally(() => {
      inflight = null
      schedule()
    })
  return inflight
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  if (listeners.size === 1) void refreshExportJobs()
  return () => {
    listeners.delete(listener)
    if (listeners.size > 0) return
    // Nobody is looking (signed out, most likely): forget it all.
    if (timer !== null) window.clearTimeout(timer)
    timer = null
    refused = false
    state = { jobs: [], loaded: false }
  }
}

export function useExportJobs(): State {
  return useSyncExternalStore(subscribe, () => state)
}

function replace(job: ExportJob) {
  set({ jobs: [job, ...state.jobs.filter((j) => j.id !== job.id)].sort((a, b) => b.createdAt.localeCompare(a.createdAt)), loaded: true })
}

export async function startExport(input: Parameters<typeof api.exports.start>[0]): Promise<ExportJob> {
  const job = await api.exports.start(input)
  refused = false
  replace(job)
  schedule()
  return job
}

export async function cancelExport(id: string): Promise<void> {
  replace(await api.exports.cancel(id))
  schedule()
}

export async function removeExport(id: string): Promise<void> {
  await api.exports.remove(id)
  set({ jobs: state.jobs.filter((j) => j.id !== id), loaded: true })
}
