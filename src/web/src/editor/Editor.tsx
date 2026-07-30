import { useEditor, EditorContent, type Editor as TiptapEditor } from '@tiptap/react'
import { useEffect, useRef } from 'react'
import { TableControls } from './TableControls'
import { TableWidthControls } from './TableWidthControls'
import { LinkMenu } from './LinkMenu'
import { SelectionBubbleMenu } from './SelectionBubbleMenu'
import { ImageHoverMenu } from './ImageHoverMenu'
import { getSharedExtensions } from './extensions'
import { handleImageDrop, handleImagePaste } from './imageUpload'
import { setSlashCommandStorage } from './slash/items'

type Props = {
  /** ProseMirror document as a JSON string. */
  value: string
  editable?: boolean
  /** Called with the updated document JSON string on every edit. */
  onChange?: (json: string) => void
  /** Resolves the page id image attachments should be uploaded against (editable mode only). */
  getUploadPageId?: () => Promise<string>
  /** Reports an image upload failure (paste/drop/toolbar), e.g. into a form's error banner. */
  onUploadError?: (message: string) => void
  /**
   * Called with the live TipTap instance once it exists (and with null on
   * unmount), so the host page can render the formatting Toolbar in its own
   * top action bar instead of inside the editor — keeping view and edit mode
   * visually consistent.
   */
  onEditorReady?: (editor: TiptapEditor | null) => void
}

function parseDoc(value: string): object | undefined {
  if (!value) return undefined
  try {
    return JSON.parse(value) as object
  } catch {
    return undefined
  }
}

/**
 * Block WYSIWYG editor (TipTap/ProseMirror). Used both for editing pages and,
 * with `editable={false}`, for rendering stored content read-only.
 */
export function Editor({ value, editable = true, onChange, getUploadPageId, onUploadError, onEditorReady }: Props) {
  // editorProps' handlers close over this ref rather than `editor` directly,
  // since they're set at useEditor's initial options and `editor` doesn't
  // exist yet at that point.
  const editorRef = useRef<TiptapEditor | null>(null)
  const editor = useEditor({
    extensions: getSharedExtensions({ editable }),
    content: parseDoc(value),
    editable,
    onUpdate: ({ editor }) => onChange?.(JSON.stringify(editor.getJSON())),
    editorProps: {
      handlePaste: (_view, event) => handleImagePaste(editorRef.current, event, getUploadPageId, onUploadError),
      handleDrop: (_view, event) => handleImageDrop(editorRef.current, event, getUploadPageId, onUploadError),
    },
  })
  useEffect(() => {
    editorRef.current = editor
  }, [editor])

  useEffect(() => {
    if (editable) onEditorReady?.(editor ?? null)
    return () => {
      if (editable) onEditorReady?.(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editor, editable])

  // The slash-command menu's Image item needs the current upload callbacks,
  // but SlashCommand is configured once in the shared extension list — so
  // instead they're handed to it via editor.storage, kept in sync here.
  useEffect(() => {
    if (!editor) return
    setSlashCommandStorage(editor, { getUploadPageId, onUploadError })
  }, [editor, getUploadPageId, onUploadError])

  // Keep the editor in sync when the source changes externally (e.g. switching
  // which version is previewed). Guarded so it never clobbers active typing.
  useEffect(() => {
    if (!editor || editable) return
    editor.commands.setContent(parseDoc(value) ?? { type: 'doc', content: [] })
  }, [editor, editable, value])

  useEffect(() => {
    editor?.setEditable(editable)
  }, [editor, editable])

  return (
    <div className={editable ? 'editor editor--editable' : 'editor'}>
      {editable && editor && <TableControls editor={editor} />}
      {editable && editor && <TableWidthControls editor={editor} />}
      {editable && editor && <LinkMenu editor={editor} />}
      {editable && editor && (
        <SelectionBubbleMenu editor={editor} getPageId={getUploadPageId} onCommentError={onUploadError} />
      )}
      {editable && editor && (
        <ImageHoverMenu editor={editor} getPageId={getUploadPageId} onCommentError={onUploadError} />
      )}
      <EditorContent editor={editor} className="editor__content" />
    </div>
  )
}

