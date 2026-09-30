import { describe, expect, it } from 'vitest'
import { PASSWORD_MAX, passwordProblem } from './passwordRule'

describe('passwordProblem', () => {
  it('accepts an ordinary long enough password', () => {
    expect(passwordProblem('correct horse battery')).toBeNull()
    expect(passwordProblem('x'.repeat(PASSWORD_MAX))).toBeNull()
  })

  it('refuses one that is too short or too long, with the server’s words', () => {
    expect(passwordProblem('Abc12!x')).toBe('Password must be at least 8 characters.')
    expect(passwordProblem('x'.repeat(PASSWORD_MAX + 1))).toBe('Password must be at most 1024 characters.')
  })

  it('refuses one that is only spaces, tabs or line breaks', () => {
    expect(passwordProblem('        ')).toBe('Password cannot be only spaces.')
    expect(passwordProblem(' \t \n    ')).toBe('Password cannot be only spaces.')
    expect(passwordProblem('  a     ')).toBeNull()
  })
})
