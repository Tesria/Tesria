import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { EmbedView } from './EmbedView'
import { SmartLinkView } from './SmartLinkView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    embed: {
      insertEmbed: (url?: string) => ReturnType
      insertSmartLink: (url?: string) => ReturnType
    }
  }
}

/**
 * A third-party page in a frame: a video, a design, a board.
 *
 * The document stores only the URL an author pasted. What actually gets
 * framed is decided by the server (`/api/embeds/resolve`), which checks the
 * instance's allowlist and narrows a known provider's URL to its embed form.
 * The client never decides what may be framed, and the CSP's `frame-src` is
 * built from the same allowlist, so an off-list host is refused twice.
 */
export const Embed = Node.create({
  name: 'embed',
  group: 'block',
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      url: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-url') ?? '',
        renderHTML: (attributes: { url?: string }) => ({ 'data-url': attributes.url ?? '' }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="embed"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'embed', class: 'embed' })]
  },

  addNodeView() {
    return ReactNodeViewRenderer(EmbedView)
  },

  addCommands() {
    return {
      insertEmbed:
        (url = '') =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs: { url } }),
      insertSmartLink:
        (url = '') =>
        ({ commands }) =>
          commands.insertContent({ type: 'smartLink', attrs: { url, display: 'card' } }),
    }
  },
})

/**
 * A link that shows what it points at: the target's own title and
 * description, fetched server-side through the SSRF guard and cached.
 *
 * Inline or card. Either way the document stores the URL and nothing else:
 * a cached title is a copy of someone else's page, and copies go stale.
 */
export const SmartLink = Node.create({
  name: 'smartLink',
  group: 'block',
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      url: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-url') ?? '',
        renderHTML: (attributes: { url?: string }) => ({ 'data-url': attributes.url ?? '' }),
      },
      display: {
        default: 'card',
        parseHTML: (element: HTMLElement) => (element.getAttribute('data-display') === 'inline' ? 'inline' : 'card'),
        renderHTML: (attributes: { display?: string }) => ({ 'data-display': attributes.display === 'inline' ? 'inline' : 'card' }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="smart-link"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    // Read-only rendering with no node view still has to be a usable link.
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'smart-link', class: 'smart-link' }),
      ['a', { href: String(node.attrs.url ?? ''), rel: 'noreferrer noopener', target: '_blank' }, String(node.attrs.url ?? '')]]
  },

  addNodeView() {
    return ReactNodeViewRenderer(SmartLinkView)
  },
})
