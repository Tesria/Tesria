import { type FormEvent, useEffect, useMemo, useState } from 'react'
import { api, type Comment } from '../../api/client'
import { Avatar } from '../../components/Avatar'
import { useAuth } from '../../auth/AuthContext'

type Node = Comment & { replies: Node[] }

function buildThreads(comments: Comment[]): Node[] {
  const nodes = new Map<string, Node>()
  for (const c of comments) nodes.set(c.id, { ...c, replies: [] })
  const roots: Node[] = []
  for (const node of nodes.values()) {
    const parent = node.parentCommentId ? nodes.get(node.parentCommentId) : undefined
    if (parent) parent.replies.push(node)
    else roots.push(node)
  }
  const byDate = (a: Node, b: Node) => a.createdAt.localeCompare(b.createdAt)
  const sort = (list: Node[]) => {
    list.sort(byDate)
    for (const n of list) sort(n.replies)
  }
  sort(roots)
  return roots
}

export function CommentsPanel({ pageId, readOnly = false }: { pageId: string; readOnly?: boolean }) {
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageId])

  const threads = useMemo(() => (comments ? buildThreads(comments) : []), [comments])

  return (
    <div className="comments">
      {error && <p className="alert alert--error">{error}</p>}
      {!readOnly && <CommentForm pageId={pageId} onAdded={reload} placeholder="Add a comment…" />}
      {comments && comments.length === 0 && <p className="muted small">No comments yet.</p>}
      <ul className="comment-list">
        {threads.map((node) => (
          <CommentItem key={node.id} node={node} pageId={pageId} onChanged={reload} readOnly={readOnly} />
        ))}
      </ul>
    </div>
  )
}

function CommentItem({ node, pageId, onChanged, readOnly = false }: { node: Node; pageId: string; onChanged: () => void; readOnly?: boolean }) {
  const { user } = useAuth()
  const [replying, setReplying] = useState(false)
  const [editing, setEditing] = useState(false)
  const [editBody, setEditBody] = useState(node.body ?? '')
  const isOwn = user?.id === node.authorId && !node.isDeleted

  async function saveEdit(e: FormEvent) {
    e.preventDefault()
    await api.comments.update(node.id, editBody)
    setEditing(false)
    onChanged()
  }

  async function remove() {
    if (!confirm('Delete this comment?')) return
    await api.comments.remove(node.id)
    onChanged()
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
        <span className="muted small">{new Date(node.createdAt).toLocaleString()}</span>
      </div>
      {editing ? (
        <form onSubmit={saveEdit} className="comment__edit">
          <textarea value={editBody} onChange={(e) => setEditBody(e.target.value)} rows={2} required />
          <div className="row-gap">
            <button type="submit" className="btn btn--primary btn--sm">Save</button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(false)}>Cancel</button>
          </div>
        </form>
      ) : (
        <p className={node.isDeleted ? 'comment__body muted' : 'comment__body'}>
          {node.isDeleted ? '[deleted]' : node.body}
        </p>
      )}
      {!node.isDeleted && !readOnly && (
        <div className="comment__actions">
          <button type="button" className="link-btn" onClick={() => setReplying((v) => !v)}>Reply</button>
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
            <CommentItem key={child.id} node={child} pageId={pageId} onChanged={onChanged} readOnly={readOnly} />
          ))}
        </ul>
      )}
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
    if (!body.trim()) return
    setBusy(true)
    try {
      await api.comments.create(pageId, { body, parentCommentId: parentCommentId ?? null })
      setBody('')
      onAdded()
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="comment-form" onSubmit={onSubmit}>
      <textarea value={body} onChange={(e) => setBody(e.target.value)} placeholder={placeholder} rows={2} />
      <button type="submit" className="btn btn--primary btn--sm" disabled={busy || !body.trim()}>
        {busy ? 'Posting…' : 'Post'}
      </button>
    </form>
  )
}
