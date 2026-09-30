import { describe, expect, it } from 'vitest'
import { localPath } from './returnPath'

describe('localPath', () => {
  it('keeps a path on this site, query and hash included', () => {
    expect(localPath('/spaces/DEMO/pages/1?x=1#h')).toBe('/spaces/DEMO/pages/1?x=1#h')
    expect(localPath('/')).toBe('/')
  })

  it('falls back for anything that could leave the site', () => {
    expect(localPath('https://evil.example')).toBe('/spaces')
    expect(localPath('//evil.example')).toBe('/spaces')
    expect(localPath('/\\evil.example')).toBe('/spaces')
    expect(localPath('javascript:alert(1)')).toBe('/spaces')
    expect(localPath('/x\nSet-Cookie')).toBe('/spaces')
  })

  it('falls back for nothing at all', () => {
    expect(localPath(null)).toBe('/spaces')
    expect(localPath('')).toBe('/spaces')
    expect(localPath(undefined, '/profile')).toBe('/profile')
  })
})
