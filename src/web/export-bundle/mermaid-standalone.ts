// The entry point for the single-file Mermaid bundle that exported HTML
// pages carry inline. Built by `vite.config.mermaid.ts` with every dynamic
// import inlined, so the result is one file that needs no network of its
// own, which is the whole point (see ExportEndpoints).
import mermaid from 'mermaid'

mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' })

const draw = () => {
  void mermaid.run({ querySelector: 'pre.mermaid' })
}

// Not `startOnLoad: true`: that only ever listens for DOMContentLoaded, so
// a bundle that finishes parsing after the document is ready, which is the
// normal case for a script inlined at the end of the body, never draws
// anything at all.
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', draw)
else draw()

export default mermaid
