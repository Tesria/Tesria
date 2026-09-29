import { describe, expect, it } from 'vitest'
import { rowState, targetState, type TargetRow } from './targetState'

const healthy = (slot: string, kind: string): TargetRow => ({
  slot, kind, present: slot === 'cloud' ? null : true, lastBackupAt: '2026-09-29T02:00:00Z',
})

describe('targetState', () => {
  it('is healthy when every repository is', () => {
    expect(targetState([healthy('cloud', 'files'), healthy('cloud', 'database')])).toEqual({ tone: 'ok', text: 'Healthy' })
  })

  it('shows a cloud database copy that has never had a backup, however well the files copy is doing (T8-007)', () => {
    const database: TargetRow = { slot: 'cloud', kind: 'database', lastBackupAt: null }
    expect(targetState([healthy('cloud', 'files'), database])).toEqual({ tone: 'warn', text: 'Nothing copied yet' })
  })

  it('shows a cloud database copy that cannot be reached (T8-008)', () => {
    const database: TargetRow = {
      ...healthy('cloud', 'database'),
      message: 'The storage provider refused the secret for this key. Check OFFSITE_CLOUD_SECRET.',
    }
    expect(targetState([healthy('cloud', 'files'), database]).tone).toBe('bad')
  })

  it('says the worst, whichever row it is on', () => {
    const files: TargetRow = { ...healthy('cloud', 'files'), lastDrillOk: false }
    const database: TargetRow = { slot: 'cloud', kind: 'database', lastBackupAt: null }
    expect(targetState([database, files])).toEqual({ tone: 'bad', text: 'The last restore drill failed' })
  })
})

describe('rowState', () => {
  it('calls a missing network drive a fault and a missing removable drive a drawer', () => {
    expect(rowState({ slot: 'nas', kind: 'files', present: false }).tone).toBe('bad')
    expect(rowState({ slot: 'removable', kind: 'files', present: false })).toEqual({ tone: 'warn', text: 'Not plugged in' })
  })

  it('does not count a removable drive’s note as a failure', () => {
    const row: TargetRow = { ...healthy('removable', 'files'), message: 'Copy complete and verified.' }
    expect(rowState(row)).toEqual({ tone: 'ok', text: 'Healthy' })
  })

  it('counts a failed copy to the network drive', () => {
    const row: TargetRow = { ...healthy('nas', 'files'), message: 'The last offsite file backup failed: disk full' }
    expect(rowState(row)).toEqual({ tone: 'bad', text: 'Needs attention' })
  })
})
