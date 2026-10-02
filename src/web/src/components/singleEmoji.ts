/**
 * Why a pasted space icon or page emoji would be refused, or null when it is
 * one glyph the server will take (QA cal-007, T3-008: "🦄🦄🦄" was saved and
 * spilled out of the icon tile). The server decides (SpaceIcons.NormalizeEmoji);
 * this says so before the round trip, in the same words.
 *
 * One glyph is one grapheme cluster, as Intl.Segmenter splits text: a joined
 * sequence (👩‍💻), a flag (🇺🇸), a skin tone (👍🏽) and a keycap (#️⃣) are
 * each one, two emoji side by side are two.
 */
export function singleEmojiProblem(raw: string): string | null {
  const value = raw.trim()
  if (value.length === 0) return 'Pick an emoji.'
  // Letters, digits and markup alone are prose, not an icon (the server's rule).
  if (![...value].some((c) => c.codePointAt(0)! > 0x7f)) return 'Pick an emoji rather than letters.'
  const glyphs = [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(value)].length
  return glyphs === 1 ? null : 'Pick a single emoji, not several.'
}
