import type { Editor as TiptapEditor } from '@tiptap/react'
import { api, ApiError } from '../api/client'

/** Uploads an image file as an attachment of the given page, then inserts it at the cursor. */
export async function uploadAndInsertImage(
  editor: TiptapEditor,
  file: File,
  getPageId: () => Promise<string>,
): Promise<void> {
  const pageId = await getPageId()
  const attachment = await api.attachments.upload(pageId, file)
  editor.chain().focus().setImage({ src: api.attachments.downloadUrl(attachment.id), alt: attachment.filename }).run()
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
  files.forEach((file) => {
    uploadAndInsertImage(editor, file, getPageId).catch((err: unknown) => reportUploadError(err, onError))
  })
  return true
}

/** Handles a drag-and-drop event carrying image files. Returns true if it consumed the event. */
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
  files.forEach((file) => {
    uploadAndInsertImage(editor, file, getPageId).catch((err: unknown) => reportUploadError(err, onError))
  })
  return true
}
