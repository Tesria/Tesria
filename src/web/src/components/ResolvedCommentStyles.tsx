import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { COMMENTS_CHANGED } from '../routes/panels/commentThreads'

/**
 * Hides the highlight of every resolved inline comment on a page (dev-plan
 * 15.3). The highlight is a mark in the document, which resolving does not
 * change, so the reading view and the editor each drop it by style instead;
 * reopening a thread brings it back. Keeps up with changes made elsewhere on
 * the page through the same event the comments tab listens for.
 */
export function ResolvedCommentStyles({ pageId }: { pageId: string }) {
  const [ids, setIds] = useState<string[]>([])

  useEffect(() => {
    let canceled = false
    const load = () => api.comments.listForPage(pageId)
      .then((list) => !canceled && setIds(list.filter((c) => c.resolvedAt).map((c) => c.id)))
      .catch(() => {})
    load()
    window.addEventListener(COMMENTS_CHANGED, load)
    return () => { canceled = true; window.removeEventListener(COMMENTS_CHANGED, load) }
  }, [pageId])

  if (ids.length === 0) return null
  // Ids are GUIDs from the server, so they are safe inside a selector.
  const selector = ids.filter((id) => /^[0-9a-f-]{36}$/i.test(id)).map((id) => `span[data-comment-id="${id}"]`).join(',\n')
  return <style>{`${selector} { background: none !important; border-bottom: none !important; cursor: text; }`}</style>
}
