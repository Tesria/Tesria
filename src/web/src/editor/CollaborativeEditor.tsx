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
import { WrapperMenu } from './WrapperMenu'
import { getSharedExtensions } from './extensions'
import { countPendingExternalEdits } from './externalEditMarks'
import { differsFromPage, hasUnpublishedChanges, normalize } from './externalEdits'
import { handleImageDrop, handleImagePaste } from './imageUpload'
import { setSlashCommandStorage } from './slash/items'
import { setDynamicBlockStorage } from './dynamicBlock'
import { DynamicBlockMenu } from './DynamicBlockMenu'
import { TocMenu } from './TocMenu'
import { InlineCommentPopover } from './InlineCommentPopover'
import type { CollabConnection } from './CollabStatus'
import { api, ApiError } from '../api/client'

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
  /**
   * The page as published when the editor opened, which `initialContent`
   * stops being as soon as anyone types (the page keeps it in step with the
   * editor). What "unpublished changes" are measured against (0.8.2).
   */
  publishedContent?: string
  /** Who is editing, so the draft can record whose unpublished changes it holds. */
  userId?: string
  displayName: string
  onChange: (json: string) => void
  /**
   * Whose unpublished changes the draft held when it opened (0.8.2), for the
   * banner that names them. Reported once, shortly after the document syncs.
   */
  onDraftState?: (state: DraftState) => void
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
   * Asks the live-editing service to show what the page now says inside this
   * draft, as tracked changes. Used when a publish is refused because the
   * page moved on. The service does it, not this browser (0.8.2): a browser
   * that reconciled on its own while offline, and the service reconciling the
   * same change on reconnect, put that change on the page twice (QA T5-015).
   * Sent when the connection is next in step, if it is not now.
   */
  requestReconcile: () => void
  /** Whether the draft says anything the published page does not (0.8.2: Close asks). */
  hasUnpublishedChanges: () => boolean
  /** Names of the other people with this page open in the editor right now. */
  editingNow: () => string[]
  /** Records a successful publish: the draft is now that version, and nobody's changes are unpublished. */
  published: (version: number) => void
}

/** Whose unpublished changes a draft held when it was opened (0.8.2). */
export type DraftState = {
  /** People other than you, and not editing it now, who changed the draft since it was last published. */
  others: string[]
  /** The draft differs from the page but nobody is recorded as having changed it (drafts from before 0.8.2). */
  unattributed: boolean
}

/** One entry per person in the shared document's `drafters` map: user id to display name. */
const DRAFTERS = 'drafters'

function parseDoc(value: string): object | undefined {
  try {
    return value ? (JSON.parse(value) as object) : undefined
  } catch {
    return undefined
  }
}

/** A color per user, so remote carets are distinguishable. */
function colorFor(name: string): string {
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
  pageId, token, initialContent, initialVersion, publishedContent, userId, displayName, onChange,
  onDraftState, getUploadPageId, onUploadError, onEditorReady, onStatusChange, onPendingExternalChange,
  onCollabReady,
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
  // A token for each connection (dev-plan 14.3): the first is the one the
  // page was opened with, every reconnect asks the app again. Tokens now last
  // ten minutes, and the sidecar closes connections when they expire or when
  // someone's access changes, so asking again is how a long session carries
  // on, and how a person who lost access is turned away with a reason.
  const firstToken = useRef<string | null>(token)
  const finalRef = useRef<'denied' | 'gone' | null>(null)
  const providerRef = useRef<HocuspocusProvider | null>(null)
  const provider = useMemo(() => {
    const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/collab`
    firstToken.current = token
    finalRef.current = null
    const fetchToken = async () => {
      const first = firstToken.current
      if (first) {
        firstToken.current = null
        return first
      }
      try {
        const fresh = await api.pages.collabToken(pageId)
        if (fresh.enabled && fresh.token) return fresh.token
      } catch (err) {
        if (err instanceof ApiError && (err.status === 401 || err.status === 403 || err.status === 404)) {
          finalRef.current = err.status === 404 ? 'gone' : 'denied'
          setStatus(finalRef.current)
          providerRef.current?.disconnect()
        }
      }
      return ''
    }
    const created = new HocuspocusProvider({ url, name: pageId, document: ydoc, token: fetchToken })
    providerRef.current = created
    return created
  }, [pageId, token, ydoc])

  useEffect(() => {
    const onStatus = ({ status }: { status: string }) => {
      // Once the app has said no, the bar keeps saying why.
      if (finalRef.current) return
      setStatus(status === 'connected' ? 'connected' : status === 'connecting' ? 'connecting' : 'disconnected')
    }
    // `status` does not fire again when the server refuses the *re*-connection,
    // which is what happens once a page is gone (dev-plan 11.3): the sidecar
    // closes the session and then turns the retry away. Without this the bar
    // would sit on "Live" for a page that no longer exists.
    const onRefused = () => { if (!finalRef.current) setStatus('disconnected') }
    provider.on('status', onStatus)
    provider.on('authenticationFailed', onRefused)
    provider.on('disconnect', onRefused)
    return () => {
      provider.off('status', onStatus)
      provider.off('authenticationFailed', onRefused)
      provider.off('disconnect', onRefused)
      // Leaving on purpose (Close, or another page), so the sidecar treats
      // the draft as idle at once rather than after its reconnect grace
      // (T5-031). A dropped connection or a closed tab sends nothing and
      // keeps the grace.
      try { provider.sendStateless(JSON.stringify({ type: 'leaving' })) } catch { /* not connected */ }
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
        // The id lets the draft banner leave out people editing right now.
        user: { name: displayName, color: colorFor(displayName), id: userId ?? null },
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
  const seedingRef = useRef(false)
  useEffect(() => {
    if (!editor) return
    const seed = () => {
      const fragment = ydoc.getXmlFragment('default')
      if (fragment.length === 0) {
        const parsed = parseDoc(initialContent)
        seedingRef.current = true
        try {
          if (parsed) editor.commands.setContent(parsed)
        } finally {
          seedingRef.current = false
        }
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

  // Who has changed the draft since it was last published (0.8.2), so the
  // next person to open it can be told whose unpublished changes it holds.
  // Written once per person, on their first change of their own: remote
  // changes arrive marked as such by the sync plugin, and seeding is not a
  // change anyone made.
  useEffect(() => {
    if (!editor || !userId) return
    const onUpdate = ({ transaction }: { transaction: { getMeta: (key: string) => unknown } }) => {
      if (!provider.isSynced || seedingRef.current) return
      const sync = transaction.getMeta('y-sync$') as { isChangeOrigin?: boolean } | undefined
      if (sync?.isChangeOrigin) return
      const drafters = ydoc.getMap<string>(DRAFTERS)
      if (drafters.get(userId) !== displayName) drafters.set(userId, displayName)
    }
    editor.on('update', onUpdate)
    return () => { editor.off('update', onUpdate) }
  }, [editor, provider, ydoc, userId, displayName])

  // The page as published, for measuring unpublished changes against. When
  // the draft is brought up to a newer version (an outside write, a
  // Discard), the page is read again so the measure moves with it.
  const publishedRef = useRef<{ version: number | null; doc: object | null }>({ version: null, doc: null })
  useEffect(() => {
    publishedRef.current = { version: initialVersion ?? null, doc: parseDoc(publishedContent ?? '') ?? null }
  }, [publishedContent, initialVersion])
  const refreshPublished = useRef<() => Promise<void>>(async () => {})
  useEffect(() => {
    const meta = ydoc.getMap('meta')
    let canceled = false
    const refresh = async () => {
      const version = meta.get('version')
      if (typeof version !== 'number' || version === publishedRef.current.version) return
      try {
        const page = await api.pages.get(pageId)
        if (canceled || page.currentVersionNumber !== meta.get('version')) return
        publishedRef.current = { version: page.currentVersionNumber, doc: parseDoc(page.contentJson) ?? null }
      } catch {
        // Measured against the older copy until the next change; the only
        // cost is a Close that asks when it need not have.
      }
    }
    refreshPublished.current = refresh
    const onMeta = () => { void refresh() }
    meta.observe(onMeta)
    return () => {
      canceled = true
      meta.unobserve(onMeta)
    }
  }, [ydoc, pageId])

  const readyRef = useRef(onCollabReady)
  readyRef.current = onCollabReady
  const draftStateRef = useRef(onDraftState)
  draftStateRef.current = onDraftState
  const statusRef = useRef(status)
  statusRef.current = status
  useEffect(() => {
    if (!editor) return
    const others = () => {
      const names: string[] = []
      provider.awareness?.getStates().forEach((state, clientId) => {
        if (clientId === ydoc.clientID) return
        const user = (state as { user?: { id?: string | null; name?: string } }).user
        if (user?.name && user.id !== userId && !names.includes(user.name)) names.push(user.name)
      })
      return names
    }
    const unpublished = (humanOnly: boolean) => {
      const page = publishedRef.current.doc
      if (!page) return false
      try {
        const draft = normalize(editor.schema, editor.getJSON())
        const published = normalize(editor.schema, page)
        return humanOnly ? differsFromPage(draft, published) : hasUnpublishedChanges(draft, published)
      } catch {
        return true
      }
    }
    let pendingReconcile = false
    const sendReconcile = () => {
      if (!pendingReconcile) return
      pendingReconcile = false
      provider.sendStateless(JSON.stringify({ type: 'reconcile' }))
    }
    const onSynced = () => sendReconcile()
    provider.on('synced', onSynced)

    const handle: CollabHandle = {
      version: () => {
        const value = ydoc.getMap('meta').get('version')
        return typeof value === 'number' ? value : null
      },
      requestReconcile: () => {
        pendingReconcile = true
        if (provider.isSynced && statusRef.current === 'connected') sendReconcile()
      },
      hasUnpublishedChanges: () => unpublished(false),
      editingNow: others,
      published: (version) => {
        ydoc.transact(() => {
          ydoc.getMap('meta').set('version', version)
          const drafters = ydoc.getMap<string>(DRAFTERS)
          for (const key of [...drafters.keys()]) drafters.delete(key)
        })
      },
    }
    readyRef.current?.(handle)

    // Whose changes the draft held on opening: once, a moment after syncing,
    // by which time the other people present have announced themselves and
    // the page has been re-read if the draft was brought up to a newer
    // version on load.
    let timer: ReturnType<typeof setTimeout> | undefined
    const report = () => {
      timer = setTimeout(async () => {
        await refreshPublished.current()
        if (!unpublished(true)) {
          draftStateRef.current?.({ others: [], unattributed: false })
          return
        }
        const present = new Set(others())
        const names: string[] = []
        ydoc.getMap<string>(DRAFTERS).forEach((name, id) => {
          if (id !== userId && !present.has(name) && !names.includes(name)) names.push(name)
        })
        const recorded = ydoc.getMap<string>(DRAFTERS).size > 0
        draftStateRef.current?.({ others: names, unattributed: !recorded })
      }, 1500)
    }
    const reportOnce = () => {
      provider.off('synced', reportOnce)
      report()
    }
    if (provider.isSynced) report()
    else provider.on('synced', reportOnce)

    return () => {
      clearTimeout(timer)
      provider.off('synced', onSynced)
      provider.off('synced', reportOnce)
      readyRef.current?.(null)
    }
  }, [editor, ydoc, provider, userId])

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
      {editor && <WrapperMenu editor={editor} />}
      {editor && <DynamicBlockMenu editor={editor} />}
      {editor && <TocMenu editor={editor} />}
      {editor && <InlineCommentPopover editor={editor} getPageId={getUploadPageId} />}
      <EditorContent editor={editor} className="editor__content" />
    </div>
  )
}
