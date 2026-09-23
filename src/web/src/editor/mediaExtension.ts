import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { AttachmentView } from './AttachmentView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    attachmentBlock: {
      /** Insert a block for an already-uploaded attachment. */
      insertAttachmentBlock: (attachmentId?: string) => ReturnType
      /** File or video, set to play its video as an animation (dev-plan 10.5 step 2). */
      insertAnimation: () => ReturnType
      insertGallery: () => ReturnType
    }
  }
}

/**
 * A page attachment shown in place: a video player, a PDF, or a file card.
 *
 * Which one is decided from the attachment's stored content type rather
 * than from an author's choice: a .mp4 is a video wherever it appears, and
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
      /**
       * How a video plays: with the player's controls, or as an animation,
       * silent, looping, starting by itself (dev-plan 10.5 step 2). Not the
       * mode the comment above rules out: that was *what to draw*, which only
       * the file can say. This is how to play a video when it is one, a
       * choice only the author can make, and it is ignored for anything else.
       */
      playback: {
        default: 'player' as 'player' | 'animation',
        parseHTML: (element: HTMLElement) => (element.getAttribute('data-playback') === 'animation' ? 'animation' : 'player'),
        renderHTML: (attributes: { playback?: string }) =>
          attributes.playback === 'animation' ? { 'data-playback': 'animation' } : {},
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
      insertAnimation:
        () =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs: { attachmentId: null, playback: 'animation' } }),
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
 * built (upload, paste, border, shadow, comments) working unchanged, and
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
