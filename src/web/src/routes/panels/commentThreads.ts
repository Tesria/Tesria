import type { Comment } from '../../api/client'

export type CommentNode = Comment & { replies: CommentNode[] }

/** Comments as threads: replies under their parent, each level oldest first. */
export function buildThreads(comments: Comment[]): CommentNode[] {
  const nodes = new Map<string, CommentNode>()
  for (const c of comments) nodes.set(c.id, { ...c, replies: [] })
  const roots: CommentNode[] = []
  for (const node of nodes.values()) {
    const parent = node.parentCommentId ? nodes.get(node.parentCommentId) : undefined
    if (parent) parent.replies.push(node)
    else roots.push(node)
  }
  const byDate = (a: CommentNode, b: CommentNode) => a.createdAt.localeCompare(b.createdAt)
  const sort = (list: CommentNode[]) => {
    list.sort(byDate)
    for (const n of list) sort(n.replies)
  }
  sort(roots)
  return roots
}

/** Find one comment anywhere in the threads. */
export function findThread(threads: CommentNode[], id: string): CommentNode | null {
  for (const t of threads) {
    if (t.id === id) return t
    const inner = findThread(t.replies, id)
    if (inner) return inner
  }
  return null
}

/**
 * Comments changed somewhere other than the Comments tab (an inline comment
 * added from the selection bubble, a reply from the popover on the
 * highlighted text): the tab listens and reloads.
 */
export const COMMENTS_CHANGED = 'tesria:comments-changed'
export function announceCommentsChanged() {
  window.dispatchEvent(new Event(COMMENTS_CHANGED))
}
