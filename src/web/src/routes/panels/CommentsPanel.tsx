import { type FormEvent, useEffect, useMemo, useState } from 'react'
import { api, type Comment } from '../../api/client'
import { Avatar } from '../../components/Avatar'
import { useConfirm } from '../../components/ConfirmDialog'
import { useAuth } from '../../auth/AuthContext'
import { CommentBody, MentionTextarea } from '../../components/MentionTextarea'
import { buildThreads, COMMENTS_CHANGED, announceCommentsChanged, type CommentNode as Node } from './commentThreads'

export function CommentsPanel({ pageId, readOnly = false, canEdit = false }: { pageId: string; readOnly?: boolean; canEdit?: boolean }) {
  const [showResolved, setShowResolved] = useState(false)
  const [comments, setComments] = useState<Comment[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  function reload() {
    api.comments
      .listForPage(pageId)
      .then(setComments)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load comments.'))
  }

  useEffect(() => {
    setComments(null)
    reload()
    // Changes made outside this tab (the inline-comment popover, the
    // selection bubble) announce themselves; reload so the list keeps up.
    window.addEventListener(COMMENTS_CHANGED, reload)
    return () => window.removeEventListener(COMMENTS_CHANGED, reload)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageId])

  const threads = useMemo(() => (comments ? buildThreads(comments) : []), [comments])
  // Resolved threads fold away (dev-plan 15.3); the count says they are there.
  const open = threads.filter((t) => !t.resolvedAt)
  const resolved = threads.filter((t) => t.resolvedAt)

  return (
    <div className="comments">
      {error && <p className="alert alert--error">{error}</p>}
      {!readOnly && <CommentForm pageId={pageId} onAdded={reload} placeholder="Add a comment…" />}
      {comments && comments.length === 0 && <p className="muted small">No comments yet.</p>}
      <ul className="comment-list">
        {open.map((node) => (
          <CommentItem key={node.id} node={node} pageId={pageId} onChanged={reload} readOnly={readOnly} canEdit={canEdit} />
        ))}
      </ul>
      {resolved.length > 0 && (
        <>
          <button type="button" className="link-btn comments__resolved-toggle" onClick={() => setShowResolved((v) => !v)}>
            {showResolved ? 'Hide resolved' : `Show resolved (${resolved.length})`}
          </button>
          {showResolved && (
            <ul className="comment-list comment-list--resolved">
              {resolved.map((node) => (
                <CommentItem key={node.id} node={node} pageId={pageId} onChanged={reload} readOnly={readOnly} canEdit={canEdit} />
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}

export function CommentItem({ node, pageId, onChanged, readOnly = false, canEdit = false }: { node: Node; pageId: string; onChanged: () => void; readOnly?: boolean; canEdit?: boolean }) {
  const { user } = useAuth()
  const [replying, setReplying] = useState(false)
  const [editing, setEditing] = useState(false)
  const [editBody, setEditBody] = useState(node.body ?? '')
  const isOwn = user?.id === node.authorId && !node.isDeleted
  const { ask, dialog } = useConfirm()

  async function toggleResolved() {
    if (node.resolvedAt) await api.comments.reopen(node.id)
    else await api.comments.resolve(node.id)
    onChanged()
    announceCommentsChanged()
  }

  async function saveEdit(e: FormEvent) {
    e.preventDefault()
    // This form can render inside the page editor's own <form> (the inline
    // comment popover); do not let the submit reach it.
    e.stopPropagation()
    await api.comments.update(node.id, editBody)
    setEditing(false)
    onChanged()
    announceCommentsChanged()
  }

  async function remove() {
    const ok = await ask({
      title: 'Delete this comment?',
      danger: true,
      confirmLabel: 'Delete the comment',
      body: <p>Replies to it stay, under a note saying this one was deleted.</p>,
    })
    if (!ok) return
    await api.comments.remove(node.id)
    onChanged()
    announceCommentsChanged()
  }

  return (
    <li className="comment">
      <div className="comment__head">
        <Avatar
          subject={{
            id: node.authorId,
            displayName: node.authorName,
            avatarHash: node.authorAvatarHash,
            avatarVariant: node.authorAvatarVariant,
          }}
          size={24}
        />
        <span className="comment__author">{node.authorName}</span>
        {node.isInline && <span className="badge">inline</span>}
        {node.resolvedAt && <span className="badge badge--resolved">resolved</span>}
        <span className="muted small">{new Date(node.createdAt).toLocaleString()}</span>
      </div>
      {node.resolvedAt && (
        <p className="muted small comment__resolved">
          Resolved{node.resolvedByName ? ` by ${node.resolvedByName}` : ''} {new Date(node.resolvedAt).toLocaleString()}
        </p>
      )}
      {editing ? (
        <form onSubmit={saveEdit} className="comment__edit">
          <MentionTextarea value={editBody} onValueChange={setEditBody} rows={2} required />
          <div className="row-gap">
            <button type="submit" className="btn btn--primary btn--sm">Save</button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(false)}>Cancel</button>
          </div>
        </form>
      ) : (
        <p className={node.isDeleted ? 'comment__body muted' : 'comment__body'}>
          {node.isDeleted ? '[deleted]' : <CommentBody text={node.body ?? ''} />}
        </p>
      )}
      {!node.isDeleted && !readOnly && (
        <div className="comment__actions">
          <button type="button" className="link-btn" onClick={() => setReplying((v) => !v)}>Reply</button>
          {node.parentCommentId === null && (isOwn || canEdit) && (
            <button type="button" className="link-btn" onClick={() => toggleResolved()}>
              {node.resolvedAt ? 'Reopen' : 'Resolve'}
            </button>
          )}
          {isOwn && (
            <>
              <button type="button" className="link-btn" onClick={() => { setEditing(true); setEditBody(node.body ?? '') }}>Edit</button>
              <button type="button" className="link-btn link-btn--danger" onClick={remove}>Delete</button>
            </>
          )}
        </div>
      )}
      {replying && (
        <CommentForm
          pageId={pageId}
          parentCommentId={node.id}
          placeholder="Write a reply…"
          onAdded={() => { setReplying(false); onChanged() }}
        />
      )}
      {node.replies.length > 0 && (
        <ul className="comment-list comment-list--nested">
          {node.replies.map((child) => (
            <CommentItem key={child.id} node={child} pageId={pageId} onChanged={onChanged} readOnly={readOnly} canEdit={canEdit} />
          ))}
        </ul>
      )}

      {dialog}
    </li>
  )
}

function CommentForm({
  pageId,
  parentCommentId,
  placeholder,
  onAdded,
}: {
  pageId: string
  parentCommentId?: string
  placeholder: string
  onAdded: () => void
}) {
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    e.stopPropagation() // see saveEdit above
    if (!body.trim()) return
    setBusy(true)
    try {
      await api.comments.create(pageId, { body, parentCommentId: parentCommentId ?? null })
      setBody('')
      onAdded()
      announceCommentsChanged()
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="comment-form" onSubmit={onSubmit}>
      <MentionTextarea value={body} onValueChange={setBody} placeholder={placeholder} rows={2} />
      <button type="submit" className="btn btn--primary btn--sm" disabled={busy || !body.trim()}>
        {busy ? 'Posting…' : 'Post'}
      </button>
    </form>
  )
}
