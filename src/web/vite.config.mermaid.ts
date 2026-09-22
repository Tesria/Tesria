import { defineConfig } from 'vite'

/**
 * Builds Mermaid into ONE self-contained file, for inlining into exported
 * HTML pages that contain a diagram.
 *
 * The app's own bundle deliberately does the opposite: it lets Mermaid
 * code-split, so a reader who opens a page with a sequence diagram
 * downloads only that diagram's renderer. An exported file has no server to
 * fetch the other chunks from, so here every dynamic import is inlined and
 * the size is accepted.
 *
 * `npm run build:mermaid` writes it into `public/`, so the normal build
 * copies it into the served assets and the API can read it off disk.
 */
export default defineConfig({
  // Without this, Vite copies public/ into this build's output too, so the
  // app's favicon and icon sprite end up duplicated inside public/export/.
  publicDir: false,
  build: {
    outDir: 'public/export',
    emptyOutDir: true,
    lib: {
      entry: 'export-bundle/mermaid-standalone.ts',
      formats: ['iife'],
      name: 'TesriaMermaid',
      fileName: () => 'mermaid-standalone.js',
    },
    rollupOptions: {
      output: {
        // One file, no chunks: an exported page cannot fetch a second one.
        inlineDynamicImports: true,
      },
    },
  },
})
