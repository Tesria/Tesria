import { defineConfig } from 'vitest/config'

/**
 * Unit tests for the parts of the editor that are logic rather than
 * rendering, added for dev-plan 8.6.
 *
 * This repo deliberately has no component or routing tests: those get a live
 * walk in a browser instead, because that is where the failures it has
 * actually shipped were found (see CLAUDE.md). What belongs here is the
 * opposite kind of code, pure functions with edge cases a walk cannot cover
 * honestly, of which the block diff in `externalEdits.ts` is the first.
 *
 * `node`, not `jsdom`: ProseMirror's model and Yjs are both plain data
 * structures, so nothing under test needs a DOM, and not pretending to have
 * one keeps the boundary clear.
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
