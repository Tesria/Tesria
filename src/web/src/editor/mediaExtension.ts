import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { AttachmentView } from './AttachmentView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    attachmentBlock: {
      /** Insert a block for an already-uploaded attachment. */
      insertAttachmentBlock: (attachmentId?: string) => ReturnType
      insertGallery: () => ReturnType
    }
  }
}

/**
 * A page attachment shown in place: a video player, a PDF, or a file card.
 *
 * Which one is decided from the attachment's stored content type rather
 * than from an author's choice — a .mp4 is a video wherever it appears, and
 * a mode attribute would just be a second source of truth that can disagree
 * with the file. The document stores the attachment id; everything else is
 * looked up, so renaming or replacing the file updates every page showing it.
 */
export const AttachmentBlock = Node.create({
  name: 'attachmentBlock',
  group: 'block',
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      attachmentId: {
        default: null as string | null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-attachment-id'),
        renderHTML: (attributes: { attachmentId?: string | null }) =>
          attributes.attachmentId ? { 'data-attachment-id': attributes.attachmentId } : {},
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="attachment-block"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'attachment-block', class: 'attachment-block' })]
  },

  addNodeView() {
    return ReactNodeViewRenderer(AttachmentView)
  },

  addCommands() {
    return {
      insertAttachmentBlock:
        (attachmentId = undefined) =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs: { attachmentId: attachmentId ?? null } }),
      insertGallery:
        () =>
        ({ commands }) =>
          commands.insertContent({ type: 'gallery', content: [{ type: 'paragraph' }] }),
    }
  },
})

/**
 * A gallery is a *layout over image nodes*, not a new kind of image: drop
 * images inside and they tile. That keeps every image affordance already
 * built — upload, paste, border, shadow, comments — working unchanged, and
 * costs the export renderer nothing, because the images inside are ordinary
 * images.
 */
export const Gallery = Node.create({
  name: 'gallery',
  group: 'block',
  content: 'block+',
  defining: true,

  parseHTML() {
    return [{ tag: 'div[data-type="gallery"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'gallery', class: 'gallery' }), 0]
  },
})
