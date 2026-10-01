// What an export job says about itself in the Downloads list (dev-plan 20.2).
import type { ExportJob } from '../api/client'

export type JobLabel = {
  /** What it is: the space and the kind of export. */
  title: string
  /** Where it is: waiting, how far along, ready, or why not. */
  status: string
  /** A smaller line under it: the current page, the error, how long it is kept. */
  detail: string | null
  /** From 0 to 1 while that is known; null for an indeterminate bar; undefined for no bar. */
  fraction?: number | null
}

const megabytes = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`

export function jobTitle(job: Pick<ExportJob, 'spaceName' | 'spaceKey' | 'format' | 'audience'>): string {
  const what = job.format === 'pack'
    ? 'Pack'
    : job.audience === 'me' ? 'Site, as you see it' : 'Site, as the public sees it'
  return `${job.spaceName || job.spaceKey}: ${what}`
}

export function jobLabel(job: ExportJob, now: Date = new Date()): JobLabel {
  const title = jobTitle(job)
  switch (job.status) {
    case 'queued':
      return {
        title,
        status: job.position && job.position > 0
          ? `Waiting: ${job.position} ${job.position === 1 ? 'export' : 'exports'} ahead`
          : 'Waiting to start',
        detail: null,
        fraction: null,
      }
    case 'running':
      if (job.total > 0)
        return {
          title,
          status: `${job.stage ?? 'Working'}: ${job.done} of ${job.total}`,
          detail: job.current,
          fraction: Math.min(1, job.done / job.total),
        }
      return { title, status: job.stage ?? 'Starting', detail: null, fraction: null }
    case 'ready': {
      const until = job.expiresAt ? new Date(job.expiresAt) : null
      const hours = until ? Math.max(0, Math.round((until.getTime() - now.getTime()) / 3_600_000)) : null
      return {
        title,
        status: job.fileSize != null ? `Ready: ${megabytes(job.fileSize)}` : 'Ready',
        detail: hours === null ? null : hours < 1 ? 'Kept for less than an hour more' : `Kept for ${hours} more ${hours === 1 ? 'hour' : 'hours'}`,
      }
    }
    case 'failed':
      return { title, status: 'Could not be prepared', detail: job.error }
    case 'canceled':
      return { title, status: 'Canceled', detail: null }
    case 'expired':
      return { title, status: 'No longer kept', detail: 'Prepare it again to download it.' }
  }
}

export const isActive = (job: Pick<ExportJob, 'status'>) => job.status === 'queued' || job.status === 'running'
