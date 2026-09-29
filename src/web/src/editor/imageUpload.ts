import type { Editor as TiptapEditor } from '@tiptap/react'
import type { Transaction } from '@tiptap/pm/state'
import { api, ApiError } from '../api/client'
import { placeImages, type ImageAttrs } from './imagePlacement'

/**
 * A document position that stays true while other changes land: an upload
 * takes a moment, and in a shared document other people keep typing.
 */
function trackPosition(editor: TiptapEditor, pos: number) {
  let current = pos
  const onTransaction = ({ transaction }: { transaction: Transaction }) => {
    current = transaction.mapping.map(current)
  }
  editor.on('transaction', onTransaction)
  return {
    get: () => current,
    stop: () => { editor.off('transaction', onTransaction) },
  }
}

/** Puts images into the editor, in order, as one step: see placeImages. */
export function insertImages(editor: TiptapEditor, images: ImageAttrs[], at?: number): void {
  if (images.length === 0 || editor.isDestroyed) return
  const tr = editor.state.tr
  if (!placeImages(tr, images, at)) return
  editor.view.dispatch(tr.scrollIntoView())
  editor.commands.focus()
}

/**
 * Uploads image files as attachments of the given page, then inserts them all
 * in the order given (see insertImages). Every file is tried; the first
 * failure is thrown after the ones that worked are on the page.
 */
export async function uploadAndInsertImages(
  editor: TiptapEditor,
  files: File[],
  getPageId: () => Promise<string>,
  at?: number,
): Promise<void> {
  if (files.length === 0) return
  const tracked = at === undefined ? null : trackPosition(editor, at)
  try {
    const pageId = await getPageId()
    const results = await Promise.allSettled(files.map((file) => api.attachments.upload(pageId, file)))
    const images = results.flatMap((r) =>
      r.status === 'fulfilled' ? [{ src: api.attachments.downloadUrl(r.value.id), alt: r.value.filename }] : [])
    insertImages(editor, images, tracked?.get())
    const failed = results.find((r): r is PromiseRejectedResult => r.status === 'rejected')
    if (failed) throw failed.reason
  } finally {
    tracked?.stop()
  }
}

function imageFiles(list: FileList | null | undefined): File[] {
  return Array.from(list ?? []).filter((f) => f.type.startsWith('image/'))
}

function reportUploadError(err: unknown, onError?: (message: string) => void): void {
  onError?.(err instanceof ApiError ? err.message : 'Image upload failed.')
}

/** Handles a paste event carrying image data. Returns true if it consumed the event. */
export function handleImagePaste(
  editor: TiptapEditor | null,
  event: ClipboardEvent,
  getPageId?: () => Promise<string>,
  onError?: (message: string) => void,
): boolean {
  if (!editor || !getPageId) return false
  const files = imageFiles(event.clipboardData?.files)
  if (files.length === 0) return false
  event.preventDefault()
  uploadAndInsertImages(editor, files, getPageId).catch((err: unknown) => reportUploadError(err, onError))
  return true
}

/**
 * Handles a drag-and-drop event carrying image files. Returns true if it
 * consumed the event. The pictures go where they were dropped, not where the
 * cursor happened to be (QA t4-012).
 */
export function handleImageDrop(
  editor: TiptapEditor | null,
  event: DragEvent,
  getPageId?: () => Promise<string>,
  onError?: (message: string) => void,
): boolean {
  if (!editor || !getPageId) return false
  const files = imageFiles(event.dataTransfer?.files)
  if (files.length === 0) return false
  event.preventDefault()
  const at = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })?.pos
  uploadAndInsertImages(editor, files, getPageId, at).catch((err: unknown) => reportUploadError(err, onError))
  return true
}
