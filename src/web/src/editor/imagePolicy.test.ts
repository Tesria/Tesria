import { describe, expect, it } from 'vitest'
import { blockedImageHost } from './imagePolicy'

const here = 'https://wiki.example.com/spaces/X/pages/1/edit'

describe('blockedImageHost', () => {
  it('blocks nothing when images are not restricted', () => {
    expect(blockedImageHost('https://tracker.example.net/p.png', null, here)).toBeNull()
  })

  it('always allows this instance, data: and blob:', () => {
    const hosts: string[] = []
    expect(blockedImageHost('/api/attachments/1/content', hosts, here)).toBeNull()
    expect(blockedImageHost('https://wiki.example.com/a.png', hosts, here)).toBeNull()
    expect(blockedImageHost('data:image/png;base64,AAAA', hosts, here)).toBeNull()
    expect(blockedImageHost('blob:https://wiki.example.com/123', hosts, here)).toBeNull()
  })

  it('matches listed hosts the way the server does', () => {
    const hosts = ['.imgur.com', 'cdn.example.org']
    expect(blockedImageHost('https://i.imgur.com/x.png', hosts, here)).toBeNull()
    expect(blockedImageHost('https://imgur.com/x.png', hosts, here)).toBeNull()
    expect(blockedImageHost('https://cdn.example.org/x.png', hosts, here)).toBeNull()
    expect(blockedImageHost('https://evil-imgur.com/x.png', hosts, here)).toBe('evil-imgur.com')
    expect(blockedImageHost('https://imgur.com.attacker.net/x.png', hosts, here)).toBe('imgur.com.attacker.net')
    expect(blockedImageHost('https://sub.cdn.example.org/x.png', hosts, here)).toBe('sub.cdn.example.org')
  })
})
