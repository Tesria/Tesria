import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * Builds the editor schema, and the reconciliation that depends on it, into
 * one headless ESM file for the collaboration sidecar (dev-plan 8.6).
 *
 * The sidecar is Node, not a browser, and it needs the *same* schema the
 * editor uses: a node declared in `extensions.ts` and missing here would be
 * dropped from every document the sidecar reconciled, silently. So it is
 * built from that file rather than restated.
 *
 * `yjs` and `y-prosemirror` stay external, because the sidecar already has
 * them and two copies of Yjs in one process do not share types: a `Y.Doc`
 * made by one is not a `Y.Doc` to the other, and the failure is confusing
 * rather than loud. Everything else, React node views included, is inlined.
 * They are dead weight in Node, and that is the price of one definition.
 *
 * `npm run build:schema` writes it to `../../collab/vendor/`, which the
 * collab image copies in. It is built, not committed, for the same reason
 * `dist/` is not: a stale copy that disagrees with `extensions.ts` is the one
 * failure this design exists to prevent.
 */
export default defineConfig({
  plugins: [react()],
  publicDir: false,
  build: {
    outDir: '../../collab/vendor',
    emptyOutDir: true,
    // Node 22 with no bundler in front of it.
    target: 'node22',
    lib: {
      entry: 'schema-bundle/collab-schema.ts',
      formats: ['es'],
      fileName: () => 'collab-schema.js',
    },
    rollupOptions: {
      external: ['yjs', 'y-prosemirror'],
      output: { inlineDynamicImports: true },
    },
  },
})
