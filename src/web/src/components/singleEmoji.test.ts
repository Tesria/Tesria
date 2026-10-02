import { describe, expect, it } from 'vitest'
import { singleEmojiProblem } from './singleEmoji'

// The same shapes as SpaceIconTests and PageEmojiTests on the server. Written
// as escapes: a test file should not carry invisible joiners and selectors.
const UNICORN = '\u{1F984}'
const TECHNOLOGIST = '\u{1F469}‍\u{1F4BB}' // joined
const FLAG = '\u{1F1FA}\u{1F1F8}' // two regional indicators, one flag
const THUMBS_TONE = '\u{1F44D}\u{1F3FD}'
const KEYCAP = '#️⃣'

describe('singleEmojiProblem', () => {
  it('takes one emoji of every shape', () => {
    for (const e of [UNICORN, TECHNOLOGIST, FLAG, THUMBS_TONE, KEYCAP, '1️⃣', '\u{1F51F}', '✔', '•', '\u{1FAE9}']) {
      expect(singleEmojiProblem(e)).toBeNull()
    }
  })
  it('trims space around one', () => {
    expect(singleEmojiProblem(`  ${UNICORN} `)).toBeNull()
  })
  it('refuses several side by side (cal-007, T3-008)', () => {
    for (const e of [UNICORN + UNICORN, UNICORN.repeat(3), FLAG + FLAG, TECHNOLOGIST + UNICORN, `${UNICORN} a`]) {
      expect(singleEmojiProblem(e)).toBe('Pick a single emoji, not several.')
    }
  })
  it('refuses letters and nothing', () => {
    expect(singleEmojiProblem('ab')).toBe('Pick an emoji rather than letters.')
    expect(singleEmojiProblem('<b>x</b>')).toBe('Pick an emoji rather than letters.')
    expect(singleEmojiProblem('   ')).toBe('Pick an emoji.')
  })
})
