import { describe, expect, it } from 'vitest'
import { mayMeanSignedOut } from './sessionCheck'

describe('mayMeanSignedOut', () => {
  it('checks after a 401 or a 404 from the API', () => {
    expect(mayMeanSignedOut(401, '/api/notifications/unread-count')).toBe(true)
    expect(mayMeanSignedOut(404, '/api/pages/0b7c')).toBe(true)
    expect(mayMeanSignedOut(404, '/api/spaces/DEMO?tree=1')).toBe(true)
  })

  it('ignores other failures', () => {
    for (const status of [400, 403, 409, 429, 500, 503]) {
      expect(mayMeanSignedOut(status, '/api/pages/0b7c')).toBe(false)
    }
  })

  it('ignores the sign-in endpoints, where a 401 is a wrong password or the check itself', () => {
    expect(mayMeanSignedOut(401, '/api/auth/login')).toBe(false)
    expect(mayMeanSignedOut(401, '/api/auth/me')).toBe(false)
    expect(mayMeanSignedOut(401, '/api/auth/me?x=1')).toBe(false)
  })

  it('ignores anything that is not the API', () => {
    expect(mayMeanSignedOut(404, '/media/logo.png')).toBe(false)
  })
})
