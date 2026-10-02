import { describe, expect, it } from 'vitest'
import { alertDetails, humanize, minutesWords } from './alertDetails'

describe('alertDetails (T7-015)', () => {
  it('reads a count with its window', () => {
    expect(alertDetails({ Failures: 20, WindowMinutes: 10 })).toEqual(['20 failed sign-ins in 10 minutes'])
    expect(alertDetails({ DistinctAccounts: 5, WindowMinutes: 10 })).toEqual(['5 different accounts tried in 10 minutes'])
    expect(alertDetails({ Lockouts: 3, WindowMinutes: 60 })).toEqual(['3 lockouts in an hour'])
    expect(alertDetails({ Denied: 100, WindowMinutes: 5 })).toEqual(['100 refused requests in 5 minutes'])
  })

  it('says what the last removal was in words', () => {
    expect(alertDetails({ Pages: 10, LastAction: 'page.trashed', WindowMinutes: 10 }))
      .toEqual(['10 pages removed in 10 minutes', 'Last: moved to the trash'])
  })

  it('never shows a field by its code name', () => {
    const lines = alertDetails({
      Failures: 20, WindowMinutes: 10, LastAction: 'page.trashed', DistinctAccounts: 5,
      Agent: 'logical', JobId: 'x', VolumeFreeBytes: 2 * 1024 ** 3, VolumeTotalBytes: 100 * 1024 ** 3,
      LastWalAt: '2026-10-01T10:00:00Z', SomethingNew: 'value',
    })
    for (const line of lines) expect(line).not.toMatch(/WindowMinutes|LastAction|DistinctAccounts|JobId|[a-z][A-Z]/)
    expect(lines).toContain('2.0 GB free of 100.0 GB')
    expect(lines).toContain('Something new: value')
  })

  it('leaves out what is shown elsewhere', () => {
    expect(alertDetails({ TopPaths: [{ Path: '/x', Count: 1 }], Unauthorized: 3, SharedAddress: false })).toEqual([])
  })
})

describe('helpers', () => {
  it('humanizes a field name', () => {
    expect(humanize('LastWalAt')).toBe('Last wal at')
    expect(humanize('Error')).toBe('Error')
  })
  it('words a window', () => {
    expect(minutesWords(1)).toBe('a minute')
    expect(minutesWords(120)).toBe('2 hours')
  })
})
