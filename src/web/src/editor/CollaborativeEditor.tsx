import { useEffect, useMemo, useRef, useState } from 'react'
import { EditorContent, useEditor, type Editor as TiptapEditor } from '@tiptap/react'
import Collaboration from '@tiptap/extension-collaboration'
import CollaborationCaret from '@tiptap/extension-collaboration-caret'
import { HocuspocusProvider } from '@hocuspocus/provider'
import * as Y from 'yjs'
import { TableControls } from './TableControls'
import { TableCellMenu } from './TableCellMenu'
import { TableWidthControls } from './TableWidthControls'
import { LinkMenu } from './LinkMenu'
import { SelectionBubbleMenu } from './SelectionBubbleMenu'
import { ImageHoverMenu } from './ImageHoverMenu'
import { StatusMenu } from './StatusMenu'
import { DateMenu } from './DateMenu'
import { LayoutMenu } from './LayoutMenu'
import { getSharedExtensions } from './extensions'
import { countPendingExternalEdits } from './externalEditMarks'
import { reconcileYDoc } from './externalEdits'
import { handleImageDrop, handleImagePaste } from './imageUpload'
import { setSlashCommandStorage } from './slash/items'
import { setDynamicBlockStorage } from './dynamicBlock'
import { DynamicBlockMenu } from './DynamicBlockMenu'
import { TocMenu } from './TocMenu'
import { InlineCommentPopover } from './InlineCommentPopover'
import type { CollabConnection } from './CollabStatus'

type Props = {
  pageId: string
  token: string
  /** Stored page content, used to seed the shared document the first time. */
  initialContent: string
  /**
   * Which published version `initialContent` is (dev-plan 8.6). Recorded in
   * the shared document so the sidecar can tell, on a later load, whether the
   * page has moved on underneath the draft.
   */
  initialVersion?: number
  displayName: string
  onChange: (json: string) => void
  /** Resolves the page id image attachments should be uploaded against. */
  getUploadPageId?: () => Promise<string>
  /** Reports an image upload failure (paste/drop/toolbar), e.g. into a form's error banner. */
  onUploadError?: (message: string) => void
  /** Called with the live TipTap instance once it exists (and with null on unmount): see Editor.tsx. */
  onEditorReady?: (editor: TiptapEditor | null) => void
  /** The session's connection state, for the page to show above the title (CollabStatus). */
  onStatusChange?: (status: CollabConnection) => void
  /**
   * How many tracked changes from outside this session are waiting to be
   * accepted or rejected (dev-plan 8.6). Drives the banner above the editor.
   */
  onPendingExternalChange?: (count: number) => void
  /**
   * Hands out the two things only this component can reach into the shared
   * document for: which page version the draft is reconciled to, and how to
   * reconcile it against a page that has moved on (the 409 path). Null on
   * unmount, like `onEditorReady`.
   */
  onCollabReady?: (handle: CollabHandle | null) => void
}

/** The shared document, for the page that owns this editor (dev-plan 8.6). */
export type CollabHandle = {
  /** The published version this draft has been brought up to date with, if known. */
  version: () => number | null
  /**
   * Shows what a published page says inside this draft, as tracked changes.
   * Used when a publish is refused because the page moved on: the editor
   * reconciles against the answer instead of overwriting it.
   */
  reconcileTo: (publishedJson: string, version: number) => void
}

function parseDoc(value: string): object | undefined {
  try {
    return value ? (JSON.parse(value) as object) : undefined
  } catch {
    return undefined
  }
}

/** A colour per user, so remote carets are distinguishable. */
function colourFor(name: string): string {
  const palette = ['#0c66e4', '#ae4787', '#216e4e', '#a54800', '#5e4db2', '#206a83']
  let hash = 0
  for (const ch of name) hash = (hash + ch.charCodeAt(0)) % palette.length
  return palette[hash]
}

/**
 * Editor backed by a shared Yjs document, so several people can edit a page at
 * once and see each other's carets. Yjs owns undo/redo history here, so the
 * StarterKit's own history is disabled to avoid the two fighting.
 */
export function CollaborativeEditor({
  pageId, token, initialContent, initialVersion, displayName, onChange, getUploadPageId,
  onUploadError, onEditorReady, onStatusChange, onPendingExternalChange, onCollabReady,
}: Props) {
  const [status, setStatus] = useState<CollabConnection>('connecting')
  useEffect(() => { onStatusChange?.(status) }, [status, onStatusChange])
  const editorRef = useRef<TiptapEditor | null>(null)

  // One document + provider per page, torn down when the page changes.
  // pageId is deliberately a dependency even though the factory doesn't read
  // it: switching pages must produce a *fresh* CRDT document, never reuse the
  // previous page's state.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const ydoc = useMemo(() => new Y.Doc(), [pageId])
  const provider = useMemo(() => {
    const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/collab`
    return new HocuspocusProvider({ url, name: pageId, document: ydoc, token })
  }, [pageId, token, ydoc])

  useEffect(() => {
    const onStatus = ({ status }: { status: string }) =>
      setStatus(status === 'connected' ? 'connected' : 'disconnected')
    // `status` does not fire again when the server refuses the *re*-connection,
    // which is what happens once a page is gone (dev-plan 11.3): the sidecar
    // closes the session and then turns the retry away. Without this the bar
    // would sit on "Live" for a page that no longer exists.
    const onRefused = () => setStatus('disconnected')
    provider.on('status', onStatus)
    provider.on('authenticationFailed', onRefused)
    provider.on('disconnect', onRefused)
    return () => {
      provider.off('status', onStatus)
      provider.off('authenticationFailed', onRefused)
      provider.off('disconnect', onRefused)
      provider.destroy()
      ydoc.destroy()
    }
  }, [provider, ydoc])

  const editor = useEditor({
    extensions: [
      ...getSharedExtensions({ collaborative: true }),
      Collaboration.configure({ document: ydoc }),
      CollaborationCaret.configure({
        provider,
        user: { name: displayName, color: colourFor(displayName) },
      }),
    ],
    onUpdate: ({ editor }) => onChange(JSON.stringify(editor.getJSON())),
    editorProps: {
      handlePaste: (_view, event) => handleImagePaste(editorRef.current, event, getUploadPageId, onUploadError),
      handleDrop: (_view, event) => handleImageDrop(editorRef.current, event, getUploadPageId, onUploadError),
    },
  }, [provider, ydoc])
  useEffect(() => {
    editorRef.current = editor
  }, [editor])

  useEffect(() => {
    onEditorReady?.(editor ?? null)
    return () => onEditorReady?.(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editor])

  // The slash-command menu's Image item needs the current upload callbacks,
  // but SlashCommand is configured once in the shared extension list, so
  // instead they're handed to it via editor.storage, kept in sync here.
  useEffect(() => {
    if (!editor) return
    setSlashCommandStorage(editor, { getUploadPageId, onUploadError })
  }, [editor, getUploadPageId, onUploadError])

  // The host page for dynamic blocks: a collaborative session always has a
  // real page id, and it is the same resolver uploads use.
  useEffect(() => {
    if (!editor) return
    setDynamicBlockStorage(editor, { getPageId: getUploadPageId ?? (() => Promise.resolve(pageId)) })
  }, [editor, getUploadPageId, pageId])

  // Seed the shared document from stored content the first time anyone opens
  // it. Guarded on emptiness so we never clobber other people's live edits.
  useEffect(() => {
    if (!editor) return
    const seed = () => {
      const fragment = ydoc.getXmlFragment('default')
      if (fragment.length === 0) {
        const parsed = parseDoc(initialContent)
        if (parsed) editor.commands.setContent(parsed)
      }
      // Which published version this draft is built on (dev-plan 8.6). The
      // sidecar reads it on a later load to tell whether the page moved on
      // while nobody had it open; without it, an API or MCP write would be
      // invisible here and the next Update would write over it.
      //
      // Only ever set, never corrected: if the sidecar has already reconciled
      // this document to a newer version, that number is the true one and
      // this client's idea of "the version I loaded" is the stale one.
      const meta = ydoc.getMap('meta')
      if (typeof initialVersion === 'number' && typeof meta.get('version') !== 'number') {
        meta.set('version', initialVersion)
      }
      // Make sure the parent has the current content even without an edit.
      onChange(JSON.stringify(editor.getJSON()))
    }
    if (provider.isSynced) seed()
    else provider.on('synced', seed)
    return () => {
      provider.off('synced', seed)
    }
  }, [editor, provider, ydoc, initialContent, initialVersion, onChange])

  // The banner's count. Recomputed on every transaction, which covers both
  // this person typing and an update arriving over the wire, since Yjs
  // applies those as transactions too.
  const pendingRef = useRef(onPendingExternalChange)
  pendingRef.current = onPendingExternalChange
  useEffect(() => {
    if (!editor) return
    const report = () => pendingRef.current?.(countPendingExternalEdits(editor.state.doc))
    report()
    editor.on('transaction', report)
    return () => { editor.off('transaction', report) }
  }, [editor])

  const readyRef = useRef(onCollabReady)
  readyRef.current = onCollabReady
  useEffect(() => {
    if (!editor) return
    const handle: CollabHandle = {
      version: () => {
        const value = ydoc.getMap('meta').get('version')
        return typeof value === 'number' ? value : null
      },
      reconcileTo: (publishedJson, version) => {
        const parsed = parseDoc(publishedJson)
        if (!parsed) return
        reconcileYDoc(ydoc, editor.schema, parsed, { source: 'page', actor: null })
        ydoc.getMap('meta').set('version', version)
      },
    }
    readyRef.current?.(handle)
    return () => readyRef.current?.(null)
  }, [editor, ydoc])

  return (
    <div className="editor editor--editable">
      {editor && <TableControls editor={editor} />}
      {editor && <TableWidthControls editor={editor} />}
      {editor && <TableCellMenu editor={editor} />}
      {editor && <LinkMenu editor={editor} />}
      {editor && <SelectionBubbleMenu editor={editor} getPageId={getUploadPageId} onCommentError={onUploadError} />}
      {editor && <ImageHoverMenu editor={editor} getPageId={getUploadPageId} onCommentError={onUploadError} />}
      {editor && <StatusMenu editor={editor} />}
      {editor && <DateMenu editor={editor} />}
      {editor && <LayoutMenu editor={editor} />}
      {editor && <DynamicBlockMenu editor={editor} />}
      {editor && <TocMenu editor={editor} />}
      {editor && <InlineCommentPopover editor={editor} getPageId={getUploadPageId} />}
      <EditorContent editor={editor} className="editor__content" />
    </div>
  )
}
