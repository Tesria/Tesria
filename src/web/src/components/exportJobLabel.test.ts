import { describe, expect, it } from 'vitest'
import type { ExportJob } from '../api/client'
import { jobLabel, jobTitle } from './exportJobLabel'

const base: ExportJob = {
  id: 'j', format: 'site', spaceKey: 'DOCS', spaceName: 'Docs', audience: 'anonymous', status: 'queued',
  stage: null, done: 0, total: 0, current: null, position: null, error: null, fileName: null, fileSize: null,
  createdAt: '2026-10-01T12:00:00Z', startedAt: null, finishedAt: null, expiresAt: null,
}

describe('export job labels', () => {
  it('names the space and the kind of export', () => {
    expect(jobTitle(base)).toBe('Docs: Site, as the public sees it')
    expect(jobTitle({ ...base, audience: 'me' })).toBe('Docs: Site, as you see it')
    expect(jobTitle({ ...base, format: 'pack', audience: null })).toBe('Docs: Pack')
    expect(jobTitle({ ...base, spaceName: '' })).toBe('DOCS: Site, as the public sees it')
  })

  it('says where a waiting job is in the line', () => {
    expect(jobLabel(base).status).toBe('Waiting to start')
    expect(jobLabel({ ...base, position: 1 }).status).toBe('Waiting: 1 export ahead')
    expect(jobLabel({ ...base, position: 3 }).status).toBe('Waiting: 3 exports ahead')
    expect(jobLabel(base).fraction).toBeNull()
  })

  it('counts a running job, or shows it is starting', () => {
    const running = { ...base, status: 'running' as const, stage: 'Capturing pages', done: 3, total: 12, current: 'Plans' }
    expect(jobLabel(running)).toMatchObject({ status: 'Capturing pages: 3 of 12', detail: 'Plans', fraction: 0.25 })
    expect(jobLabel({ ...running, total: 0, stage: null }).status).toBe('Starting')
  })

  it('says how big a ready file is and how long it is kept', () => {
    const ready = { ...base, status: 'ready' as const, fileSize: 3 * 1024 * 1024, expiresAt: '2026-10-02T12:00:00Z' }
    const label = jobLabel(ready, new Date('2026-10-01T12:00:00Z'))
    expect(label.status).toBe('Ready: 3.0 MB')
    expect(label.detail).toBe('Kept for 24 more hours')
    expect(label.fraction).toBeUndefined()
    expect(jobLabel({ ...ready, fileSize: 2000 }, new Date('2026-10-02T11:40:00Z'))).toMatchObject({
      status: 'Ready: 2 KB', detail: 'Kept for less than an hour more',
    })
  })

  it('says why a failed job failed', () => {
    expect(jobLabel({ ...base, status: 'failed', error: 'Your role no longer allows exporting.' })).toMatchObject({
      status: 'Could not be prepared', detail: 'Your role no longer allows exporting.',
    })
  })
})
