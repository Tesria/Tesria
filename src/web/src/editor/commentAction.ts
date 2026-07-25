import type { Editor as TiptapEditor } from '@tiptap/react'
import { api } from '../api/client'

/**
 * Creates a real page comment (already-supported AnchorJson/isInline on the
 * backend), then marks the given text range with the `comment` mark carrying
 * that comment's id — this is what lets the read view highlight the
 * commented text and is the anchor linking the mark back to the row.
 */
export async function addInlineTextComment(
  editor: TiptapEditor,
  getPageId: () => Promise<string>,
  body: string,
  range: { from: number; to: number },
): Promise<void> {
  const pageId = await getPageId()
  const comment = await api.comments.create(pageId, { body, anchorJson: JSON.stringify({ type: 'text' }) })
  editor.chain().focus().setTextSelection(range).setMark('comment', { commentId: comment.id }).run()
}

/**
 * Images are atom nodes — they don't carry marks the way text does, so a
 * comment on an image has no in-document highlight, just a real comment
 * (viewable in the page's Comments panel) anchored to that image's src.
 */
export async function addImageComment(
  getPageId: () => Promise<string>,
  body: string,
  imageSrc: string,
): Promise<void> {
  const pageId = await getPageId()
  await api.comments.create(pageId, { body, anchorJson: JSON.stringify({ type: 'image', src: imageSrc }) })
}
