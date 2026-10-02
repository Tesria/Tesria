import { describe, expect, it } from 'vitest'
import { fallbackMessage, isNetworkFailure, waitWords } from './errorMessages'

describe('isNetworkFailure', () => {
  it('reads every browser\'s fetch rejection as no answer (t4-024, T5-011)', () => {
    expect(isNetworkFailure(new TypeError('Failed to fetch'))).toBe(true)
    expect(isNetworkFailure(new TypeError('Load failed'))).toBe(true)
    expect(isNetworkFailure(new TypeError('NetworkError when attempting to fetch resource.'))).toBe(true)
  })

  it('leaves an abort and anything else alone', () => {
    expect(isNetworkFailure(new DOMException('The user aborted a request.', 'AbortError'))).toBe(false)
    expect(isNetworkFailure(new Error('Failed to fetch'))).toBe(false)
    expect(isNetworkFailure('Failed to fetch')).toBe(false)
  })
})

describe('fallbackMessage', () => {
  it('never shows a bare status code', () => {
    for (const status of [400, 405, 418, 500, 501, 502, 503, 504, 507]) {
      expect(fallbackMessage(status)).not.toMatch(/\(\d{3}\)|Request failed/)
    }
  })

  it('says the server could not be reached through a proxy apart from a server fault', () => {
    expect(fallbackMessage(502)).toMatch(/not answering/)
    expect(fallbackMessage(500)).toMatch(/went wrong/)
  })

  it('says the real wait for a 429 when the server gave one (t6-016)', () => {
    expect(fallbackMessage(429, 2400)).toBe('Too many attempts. Try again in 40 minutes.')
    expect(fallbackMessage(429, 3600)).toBe('Too many attempts. Try again in an hour.')
    expect(fallbackMessage(429)).toBe('Too many attempts. Wait a minute and try again.')
  })
})

describe('waitWords', () => {
  it('rounds up to whole minutes', () => {
    expect(waitWords(1)).toBe('a minute')
    expect(waitWords(60)).toBe('a minute')
    expect(waitWords(61)).toBe('2 minutes')
    expect(waitWords(3600)).toBe('an hour')
    expect(waitWords(3 * 3600)).toBe('3 hours')
  })
})
