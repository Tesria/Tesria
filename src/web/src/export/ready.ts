/**
 * The signal an export capture waits for (dev-plan 12.1).
 *
 * The sidecar loads a real page and prints it, so it needs to know when the
 * page has finished becoming itself: the editor mounted, images fetched,
 * Mermaid drawn, KaTeX typeset, every dynamic block answered. Waiting a fixed
 * number of milliseconds would be a guess that is too long on every page and
 * too short on one of them.
 *
 * Two conditions, both required. Nothing *known* to be outstanding, judged by
 * the markers the node views leave in the DOM; and then a quiet period with
 * no mutations at all, which catches the work this file has never heard of.
 * A new node view that renders late is therefore covered by the second
 * condition even if nobody remembers to teach it to the first.
 */

/** Set on `<html>` when the page has settled, or given up waiting. */
export const READY_ATTRIBUTE = 'data-export-ready'

export type ReadyState = 'ready' | 'timeout' | 'error'

type Options = {
  /** How long the DOM must be still before the page counts as settled. */
  quietMs?: number
  /** Total budget. Past this the page reports `timeout` rather than hanging. */
  timeoutMs?: number
}

/** Work this file knows how to recognize as outstanding. */
function outstanding(root: ParentNode): string | null {
  // A diagram that has neither drawn nor failed still says this.
  if (root.querySelector('.mermaid__note')) return 'mermaid'
  // A dynamic block that has not answered.
  const loading = [...root.querySelectorAll('.dynamic-block__note')]
    .some((n) => /^loading/i.test(n.textContent?.trim() ?? ''))
  if (loading) return 'dynamic-block'
  // Math that has not typeset. An empty marker is the editing placeholder,
  // which read-only mode renders blank, so only a missing render counts.
  const mathPending = [...root.querySelectorAll('.math__rendered')]
    .some((n) => !n.querySelector('.katex') && !n.querySelector('.math__error'))
  if (mathPending) return 'math'
  // An image still in flight. A broken one is finished, not pending.
  const images = [...root.querySelectorAll('img')]
  if (images.some((img) => !img.complete)) return 'image'
  return null
}

/**
 * Resolves once the page has settled. Never rejects: an export of a page
 * whose diagram will not draw should still be an export.
 */
export function waitForSettled(root: ParentNode, options: Options = {}): Promise<ReadyState> {
  const quietMs = options.quietMs ?? 400
  const timeoutMs = options.timeoutMs ?? 20_000

  return new Promise<ReadyState>((resolve) => {
    const started = Date.now()
    let lastMutation = Date.now()
    let done = false

    const observer = new MutationObserver(() => { lastMutation = Date.now() })
    observer.observe(document.body, {
      subtree: true, childList: true, characterData: true, attributes: true,
    })

    const finish = (state: ReadyState) => {
      if (done) return
      done = true
      observer.disconnect()
      window.clearInterval(timer)
      resolve(state)
    }

    const timer = window.setInterval(() => {
      if (Date.now() - started > timeoutMs) {
        finish('timeout')
        return
      }
      // Fonts matter for a PDF: text measured against a fallback and then
      // reflowed is how a line ends up in the wrong place.
      if (document.fonts && document.fonts.status !== 'loaded') return
      if (outstanding(root)) return
      if (Date.now() - lastMutation < quietMs) return
      finish('ready')
    }, 100)
  })
}

/** Publishes the result where a capturing browser can see it. */
export function publishReady(state: ReadyState) {
  document.documentElement.setAttribute(READY_ATTRIBUTE, state)
}
