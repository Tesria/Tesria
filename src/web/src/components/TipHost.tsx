import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Clip } from './Clip'
import { chooseTip, contextFor, type Tip, type TipState } from '../onboarding/tips'
import {
  counts, currentPage, hasPastedFormatting, noteTipShown, setSignalUser,
  tipsShownToday, visitsTo,
} from '../onboarding/signals'

/** At most this many in one person's day. */
const DAILY_CAP = 3
/** How long the "Turn off tips" undo stays offered. */
const UNDO_SECONDS = 10
/** A breath after arriving, so a tip does not race the page it points at. */
const SETTLE_MS = 1200

/**
 * Shows one tip at a time (dev-plan 10.3).
 *
 * The restraint is the feature. One at a time, three a day, never over a
 * dialog or inside the wizard, never while a menu is open or the editor is
 * mid-selection, and never a tip whose control is not actually on screen.
 * A tip that cannot be shown politely is dropped, not queued.
 */
export function TipHost() {
  const { user, refresh } = useAuth()
  const location = useLocation()
  const [tip, setTip] = useState<Tip | null>(null)
  const [undo, setUndo] = useState(false)
  const shownThisSession = useRef(new Set<string>())

  useEffect(() => { setSignalUser(user?.id ?? null) }, [user?.id])

  const dismissed = new Set(user?.onboarding?.dismissedTips ?? [])

  /** Everything a trigger may ask about, gathered at the moment of asking. */
  const gather = useCallback((): TipState => {
    const c = counts()
    const page = currentPage()
    const selection = window.getSelection()?.toString().trim() ?? ''
    const article = document.querySelector('article')
    return {
      ...c,
      visitsToThisPage: page ? visitsTo(page.id) : 0,
      selectionWords: selection ? selection.split(/\s+/).length : 0,
      editorFocused: document.querySelector('.ProseMirror') !== null,
      pagesInSpace: document.querySelectorAll('.sidebar .tree-section a').length,
      pageHasComments: document.querySelectorAll('.comment-list li').length > 0,
      pageHasLabels: document.querySelectorAll('.label-chip, .labels a').length > 0,
      pageByOther: !!page && !!user && page.createdById !== user.id,
      pageIsMine: !!page && !!user && page.createdById === user.id,
      pageHasTable: !!article?.querySelector('table'),
      longList: document.querySelectorAll('.ProseMirror li').length >= 3,
      pastedFormatting: hasPastedFormatting(),
      hasCollaborators:
        document.querySelectorAll('.comment-list li').length > 0
        || document.querySelectorAll('.collab-caret, .collab-avatar').length > 0,
      twoFactorOff: user?.totpEnabled === false,
    }
  }, [user])

  /** Reasons not to speak right now, whatever the tip would be. */
  const inconvenient = useCallback(() => {
    if (!user || !user.onboarding?.tipsEnabled) return true
    if (user.setupRequired) return true
    if (document.querySelector('[role="dialog"]')) return true
    if (document.querySelector('.slash-menu, .suggest-menu, .toolbar-dropdown__menu')) return true
    if ((window.getSelection()?.toString().length ?? 0) > 0
      && document.activeElement?.closest('.ProseMirror')) return true
    if (tipsShownToday() >= DAILY_CAP) return true
    return false
  }, [user])

  useEffect(() => {
    setTip(null)
    const context = contextFor(location.pathname)
    if (!context) return

    const timer = window.setTimeout(() => {
      if (inconvenient()) return
      const chosen = chooseTip(context, gather(), dismissed)
      if (!chosen || shownThisSession.current.has(chosen.key)) return
      shownThisSession.current.add(chosen.key)
      noteTipShown()
      setTip(chosen)
    }, SETTLE_MS)
    return () => window.clearTimeout(timer)
    // `dismissed` is derived from user, which is already a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.pathname, location.search, user, gather, inconvenient])

  async function gotIt() {
    const key = tip?.key
    setTip(null)
    if (!key) return
    try {
      await api.auth.updateOnboarding({ dismissTip: key })
      await refresh()
    } catch {
      // Already out of the way for this session; it can be dismissed again.
    }
  }

  async function turnOff() {
    setTip(null)
    setUndo(true)
    window.setTimeout(() => setUndo(false), UNDO_SECONDS * 1000)
    try {
      await api.auth.updateOnboarding({ tipsEnabled: false })
      await refresh()
    } catch { /* the toggle is in the profile too */ }
  }

  async function turnBackOn() {
    setUndo(false)
    try {
      await api.auth.updateOnboarding({ tipsEnabled: true })
      await refresh()
    } catch { /* as above */ }
  }

  if (undo) {
    return (
      <div className="tip tip--undo" role="status">
        <span>Tips are off.</span>
        <button type="button" className="link-btn" onClick={turnBackOn}>Undo</button>
      </div>
    )
  }

  if (!tip) return null

  return (
    <aside className="tip" role="note" aria-label={tip.title}>
      {tip.clip && <Clip name={tip.clip} className="tip__clip" />}
      <div className="tip__body">
        <strong>{tip.title}</strong>
        <p className="muted small">{tip.body}</p>
        <div className="tip__actions">
          <button type="button" className="btn btn--primary btn--sm" onClick={gotIt}>Got it</button>
          <button type="button" className="link-btn" onClick={turnOff}>Turn off tips</button>
        </div>
      </div>
    </aside>
  )
}
