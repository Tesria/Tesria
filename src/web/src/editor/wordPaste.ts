import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'

/**
 * Lists pasted from Microsoft Word (t4-021).
 *
 * Word does not put a list on the clipboard as `<ul>`/`<ol>`. Each item is a
 * paragraph whose style says `mso-list:l0 level1 lfo1` (which list, how
 * deep), and the bullet or number is written as text inside a conditional
 * comment only Word itself honors:
 *
 *   <p style='mso-list:l0 level1 lfo1'><![if !supportLists]><span>·<span>&nbsp;&nbsp; </span></span><![endif]>First</p>
 *
 * A browser reads the conditional comment as an ordinary one and keeps the
 * span, so the paste arrived as paragraphs starting with "·" and spaces.
 * This turns each run of those paragraphs into real, nested lists before
 * ProseMirror parses the paste. Google Docs and web pages already paste
 * real lists and are left alone.
 */

/** Word's marker block, in both spellings Word writes (older `<![if]>`, newer `<!--[if]-->`). */
const MARKER_BLOCK = /<!(?:--)?\[if !supportLists\](?:--)?>([\s\S]*?)<!(?:--)?\[endif\](?:--)?>/gi

/**
 * Replaces each of Word's list-marker blocks with an empty span that carries
 * the marker's text ("·", "1.", "a)"), so the item keeps its words and loses
 * the bullet, and the marker still says which kind of list it was.
 */
export function extractWordMarkers(html: string): string {
  return html.replace(MARKER_BLOCK, (_all, inner: string) => {
    const text = inner
      .replace(/<[^>]*>/g, '')
      .replace(/&nbsp;|&#160;|\u00a0/gi, ' ')
      .trim()
    return `<span data-word-marker="${text.replace(/&/g, '&amp;').replace(/"/g, '&quot;')}"></span>`
  })
}

/** Which list a paragraph belongs to and how deep, from its `mso-list` style; null when it is not a list item. */
export function msoListInfo(style: string | null): { list: string; level: number } | null {
  const m = /mso-list\s*:\s*(l\d+)\s+level(\d+)/i.exec(style ?? '')
  return m ? { list: m[1].toLowerCase(), level: Math.max(1, Number(m[2])) } : null
}

/**
 * A numbered marker ("1.", "a)", "iv.", "(2)") makes a numbered list; any
 * other ("·", "o", "§", "-", Symbol and Wingdings glyphs) a bulleted one.
 * The number is where the list starts, when it is one.
 */
export function markerKind(marker: string): { ordered: boolean; start: number } {
  const m = /^\(?([0-9]{1,4}|[a-z]{1,4})[.)]$/i.exec(marker.trim())
  if (!m) return { ordered: false, start: 1 }
  return { ordered: true, start: /^\d+$/.test(m[1]) ? Number(m[1]) : 1 }
}

/** Word's list paragraphs as real lists; any other HTML comes back unchanged. */
export function transformWordLists(html: string): string {
  if (!/mso-list/i.test(html) || typeof DOMParser === 'undefined') return html
  const doc = new DOMParser().parseFromString(extractWordMarkers(html), 'text/html')
  // Paragraphs only: a numbered Word heading stays a heading.
  const items = Array.from(doc.body.querySelectorAll<HTMLElement>('p'))
    .filter((el) => msoListInfo(el.getAttribute('style')) !== null)
  if (items.length === 0) return html

  const done = new Set<HTMLElement>()
  for (const first of items) {
    if (done.has(first)) continue
    // A run: this item and the list paragraphs straight after it
    // (element siblings, so the whitespace between them does not count).
    const run: HTMLElement[] = [first]
    const listId = msoListInfo(first.getAttribute('style'))!.list
    for (let next = first.nextElementSibling as HTMLElement | null; next; next = next.nextElementSibling as HTMLElement | null) {
      const info = msoListInfo(next.getAttribute('style'))
      if (!info || info.list !== listId) break
      run.push(next)
    }
    run.forEach((el) => done.add(el))
    buildLists(doc, run)
  }
  return doc.body.innerHTML
}

function buildLists(doc: Document, run: HTMLElement[]) {
  const out = doc.createDocumentFragment()
  const stack: { level: number; ordered: boolean; list: HTMLElement }[] = []

  for (const para of run) {
    const { level } = msoListInfo(para.getAttribute('style'))!
    let marker = ''
    const placeholder = para.querySelector('[data-word-marker]')
    if (placeholder) {
      marker = placeholder.getAttribute('data-word-marker') ?? ''
      placeholder.remove()
    } else {
      // Some Word builds skip the conditional comment and mark the span instead.
      const ignored = para.querySelector<HTMLElement>('[style*="mso-list:Ignore" i], [style*="mso-list: Ignore" i]')
      if (ignored) {
        marker = (ignored.textContent ?? '').replace(/\u00a0/g, ' ').trim()
        ignored.remove()
      }
    }
    const { ordered, start } = markerKind(marker)

    const li = doc.createElement('li')
    const p = doc.createElement('p')
    while (para.firstChild) p.appendChild(para.firstChild)
    li.appendChild(p)

    const top = () => stack[stack.length - 1]
    while (stack.length > 0 && top().level > level) stack.pop()
    // Same depth, other kind (bullets then numbers): a new list beside it.
    if (stack.length > 0 && top().level === level && top().ordered !== ordered) stack.pop()
    if (stack.length === 0 || top().level < level) {
      const list = doc.createElement(ordered ? 'ol' : 'ul')
      if (ordered && start > 1) list.setAttribute('start', String(start))
      const parent = top()
      // A deeper list goes inside the item above it, as HTML nests lists.
      if (parent) (parent.list.lastElementChild ?? parent.list).appendChild(list)
      else out.appendChild(list)
      stack.push({ level, ordered, list })
    }
    top().list.appendChild(li)
  }

  run[0].replaceWith(out)
  for (const para of run.slice(1)) para.remove()
}

export const WordPaste = Extension.create({
  name: 'wordPaste',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: new PluginKey('wordPaste'),
        props: { transformPastedHTML: transformWordLists },
      }),
    ]
  },
})
