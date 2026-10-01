import { describe, expect, it } from 'vitest'
import { downloadName } from './downloadName'

describe('downloadName', () => {
  it('reads the plain and the encoded file name', () => {
    expect(downloadName('attachment; filename="Plans.pdf"')).toBe('Plans.pdf')
    expect(downloadName('attachment; filename=Plans.pdf')).toBe('Plans.pdf')
    expect(downloadName("attachment; filename=\"Caf_.pdf\"; filename*=UTF-8''Caf%C3%A9.pdf")).toBe('Café.pdf')
  })

  it('falls back when the encoded one is broken, and gives null for none', () => {
    expect(downloadName("attachment; filename=\"a.pdf\"; filename*=UTF-8''%E0%A4%A.pdf")).toBe('a.pdf')
    expect(downloadName('attachment')).toBeNull()
    expect(downloadName(null)).toBeNull()
  })
})
