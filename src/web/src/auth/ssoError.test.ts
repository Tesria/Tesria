import { describe, expect, it } from 'vitest'
import { ssoErrorMessage } from './ssoError'

describe('ssoErrorMessage', () => {
  it('maps each known code to its own message', () => {
    for (const code of ['invite_only', 'email_not_verified', 'no_email', 'failed']) {
      expect(ssoErrorMessage(code)).toEqual(expect.any(String))
    }
    expect(ssoErrorMessage('invite_only')).toContain('invite only')
  })

  it('shows nothing for no code', () => {
    expect(ssoErrorMessage(null)).toBeNull()
    expect(ssoErrorMessage(undefined)).toBeNull()
    expect(ssoErrorMessage('')).toBeNull()
  })

  it('never shows text it was not written with', () => {
    expect(ssoErrorMessage('Your password has expired. Call 555-0100.')).toBeNull()
    expect(ssoErrorMessage('<img src=x onerror=alert(1)>')).toBeNull()
    // Names inherited by every object are not codes either.
    expect(ssoErrorMessage('toString')).toBeNull()
    expect(ssoErrorMessage('__proto__')).toBeNull()
    expect(ssoErrorMessage('constructor')).toBeNull()
  })
})
