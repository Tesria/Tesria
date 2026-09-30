import { type FormEvent, useEffect, useRef, useState } from 'react'
import { useBlocker, useNavigate } from 'react-router-dom'
import { api, ApiError, LIMITS, Permission, type Space, type SpaceExports } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { SpaceIconPicker } from '../components/SpaceIconPicker'
import { useConfirm } from '../components/ConfirmDialog'
import { DeleteSpaceDialog } from './DeleteSpaceDialog'
import { SiteExportSection } from '../components/SiteExportSection'
import { PackExportSection } from '../components/PackExportSection'
import { useSpaceContext } from './SpacePage'
import { SpaceTreeStyle } from '../components/treeMarkers'

/** The three page tree styles (dev-plan 15.8), each with a small preview. */
const TREE_STYLES: { value: SpaceTreeStyle; label: string; sample: string[] }[] = [
  { value: SpaceTreeStyle.Plain, label: 'Plain', sample: ['Getting started', '\u00a0\u00a0Quick start', 'User manual'] },
  { value: SpaceTreeStyle.Numbered, label: 'Numbered', sample: ['1  Getting started', '\u00a0\u00a01.1  Quick start', '2  User manual'] },
  { value: SpaceTreeStyle.Bulleted, label: 'Bulleted', sample: ['•  Getting started', '\u00a0\u00a0◦  Quick start', '•  User manual'] },
]

/**
 * A space's own settings: what it is called, and how it looks (dev-plan 6).
 *
 * The name and description endpoint existed since Phase 2 with nothing in the
 * UI reaching it; the icon needed somewhere to live, so this page finally
 * gives both a home. It is the Details tab of `SpaceSettingsLayout`, which
 * owns the heading and the tab row: permissions, webhooks and trash are
 * the other three tabs.
 */
export function SpaceSettingsPage() {
  const { space, onSpaceChanged } = useSpaceContext()
  const { can } = useAuth()
  const navigate = useNavigate()
  const [deleting, setDeleting] = useState(false)
  const [name, setName] = useState(space.name)
  const [description, setDescription] = useState(space.description ?? '')
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exports, setExports] = useState<SpaceExports>(space.exports)
  const { ask, dialog } = useConfirm()
  // The icon, the name and the page tree change the space itself, and so
  // does archiving it: a space administrator's to do. Anyone else was shown
  // all four and refused after using them (QA T3-012).
  const mayChange = space.canAdmin !== false

  /**
   * After any save. Only Details' own Save puts the saved name and
   * description back in their boxes: every other save (an icon, a tree
   * style, the exports) used to do it too, silently throwing away a name
   * typed and not yet saved (QA cal-004, T3-004).
   */
  function applied(updated: Space, message: string, details = false) {
    onSpaceChanged(updated)
    if (details) {
      setName(updated.name)
      setDescription(updated.description ?? '')
    }
    setStatus(message)
    setError(null)
  }

  // A name or description typed and not saved is asked about before it is
  // lost to another tab, another page or a reload (QA T3-004).
  const unsaved = mayChange && (name.trim() !== space.name || description.trim() !== (space.description ?? ''))
  const unsavedRef = useRef(unsaved)
  unsavedRef.current = unsaved
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    unsavedRef.current && currentLocation.pathname !== nextLocation.pathname)
  const blockerRef = useRef(blocker)
  blockerRef.current = blocker
  useEffect(() => {
    if (blocker.state !== 'blocked') return
    void ask({
      title: 'Leave Without Saving?',
      body: <p>The name and description you typed are not saved. Leaving puts them back as they were.</p>,
      confirmLabel: 'Leave Without Saving',
    }).then((leave) => {
      const b = blockerRef.current
      if (b.state !== 'blocked') return
      if (leave) b.proceed()
      else b.reset()
    })
  }, [blocker.state, ask])
  useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (!unsavedRef.current) return
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  /** The reversible alternative to deleting, and the only one a space's own
   *  administrator has. The endpoint has existed since Phase 2 with nothing
   *  in the UI reaching it; 11.3's delete dialog offers it, so it needs to be
   *  somewhere real to offer. */
  async function setArchived(archived: boolean) {
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      const updated = archived
        ? await api.spaces.archive(space.key)
        : await api.spaces.unarchive(space.key)
      applied(updated, archived ? 'Space archived.' : 'Space unarchived.')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the setting.')
    } finally {
      setBusy(false)
    }
  }

  /** Which formats this space may be exported in (dev-plan 12.3). */
  async function saveExports(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      const updated = await api.spaces.setExports(space.key, exports)
      applied(updated, 'Export settings saved.')
      setExports(updated.exports)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the export settings.')
    } finally {
      setBusy(false)
    }
  }

  /** The page tree's style (dev-plan 15.8). Saved on the choice, like the icon. */
  async function saveTreeStyle(treeStyle: SpaceTreeStyle) {
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      applied(await api.spaces.update(space.key, { name: space.name, description: space.description, treeStyle }), 'Page tree updated.')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  async function saveDetails(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      applied(await api.spaces.update(space.key, { name, description: description || null }), 'Saved.', true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}
      {!mayChange && (
        <p className="alert alert--error">
          You need admin rights on this space to change its icon, name, description or page tree, or to archive it.
        </p>
      )}

      {mayChange && (<>
      <section className="profile__section profile__section--wide">
        <h2>Icon</h2>
        <p className="muted small">
          Shown wherever this space appears: the spaces list, the sidebar and the
          breadcrumb.
        </p>
        <SpaceIconPicker space={space} onChanged={(updated) => applied(updated, 'Icon updated.')} />
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Details</h2>
        <form onSubmit={saveDetails}>
          <label>
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} required maxLength={LIMITS.spaceName} />
          </label>
          <label>
            Description
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What this space is for"
            />
          </label>
          <p className="muted small">
            The key <code>{space.key}</code> is part of every page&rsquo;s address and cannot change.
          </p>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Saving…' : 'Save'}
          </button>
        </form>
      </section>

      <section className="profile__section profile__section--wide" id="page-tree">
        <h2>Page Tree</h2>
        <p className="muted small">
          How the sidebar marks this space&rsquo;s pages. Numbers and bullets are only drawn beside the titles: they
          are not part of any title or address, and they follow the tree as pages are added and moved.
        </p>
        <fieldset className="tree-style" disabled={busy}>
          <legend className="sr-only">Page Tree Style</legend>
          {TREE_STYLES.map((option) => (
            <label key={option.value} className={(space.treeStyle ?? SpaceTreeStyle.Plain) === option.value ? 'tree-style__option is-active' : 'tree-style__option'}>
              <input
                type="radio"
                name="tree-style"
                checked={(space.treeStyle ?? SpaceTreeStyle.Plain) === option.value}
                onChange={() => void saveTreeStyle(option.value)}
              />
              <span className="tree-style__name">{option.label}</span>
              <span className="tree-style__sample" aria-hidden="true">
                {option.sample.map((line) => <span key={line}>{line}</span>)}
              </span>
            </label>
          ))}
        </fieldset>
      </section>
      </>)}

      {can(Permission.SpacesExports) && (
        <section className="profile__section profile__section--wide" id="exports">
          <h2>Exports</h2>
          <p className="muted small">
            Which ways this space can be downloaded. Turn a format off for a space that is more
            sensitive than the rest: it stops for everyone, administrators included, until someone with
            this setting turns it back on. People who can read a page can still copy what they read.
          </p>
          <form onSubmit={saveExports} className="space-exports">
            {([
              ['markdown', 'Markdown', 'A page as a Markdown file.'],
              ['html', 'HTML', 'A page as a single HTML file.'],
              ['pdf', 'PDF', 'A page as a PDF.'],
              ['site', 'Website', 'The whole space as a static website.'],
              ['pack', 'Wiki Pack', 'The whole space with its history, for moving it to another Tesria.'],
            ] as const).map(([key, label, hint]) => (
              <label key={key} className="admin__toggle">
                <input
                  type="checkbox"
                  checked={exports[key]}
                  onChange={(e) => setExports((x) => ({ ...x, [key]: e.target.checked }))}
                />
                <span>
                  <strong>{label}</strong>
                  <span className="muted small"> {hint}</span>
                </span>
              </label>
            ))}
            <button type="submit" className="btn btn--primary" disabled={busy
              || JSON.stringify(exports) === JSON.stringify(space.exports)}>
              {busy ? 'Saving…' : 'Save'}
            </button>
          </form>
        </section>
      )}

      {can(Permission.PagesExport) && space.exports.site && (
        <section className="profile__section profile__section--wide" id="export">
          <h2>Export as a Site</h2>
          <SiteExportSection spaceKey={space.key} />
        </section>
      )}

      {can(Permission.PagesExport) && space.exports.pack && (
        <section className="profile__section profile__section--wide" id="pack">
          <h2>Export as a Pack</h2>
          <PackExportSection spaceKey={space.key} />
        </section>
      )}

      {mayChange && (
      <section className="profile__section profile__section--wide" id="archive">
        <h2>Archive</h2>
        <p className="muted small">
          {space.archived
            ? 'This space is archived. It stays out of the spaces list until you bring it back, and nothing in it has been touched.'
            : 'Keeps the space and everything in it, out of the way: an archived space is hidden from the spaces list and from public reading, and Admin → Spaces still lists it. Reversible at any time.'}
        </p>
        <button type="button" className="btn" disabled={busy} onClick={() => setArchived(!space.archived)}>
          {space.archived ? 'Unarchive This Space' : 'Archive This Space'}
        </button>
      </section>
      )}

      {/* Last on the page and visually apart, because nothing else here is
          irreversible (dev-plan 11.3). The right is an instance one, so a
          space's own administrator sees only archiving. */}
      {can(Permission.SpacesDelete) && (
        <section className="profile__section profile__section--wide danger-zone">
          <h2>Danger Zone</h2>
          <p className="muted small">
            Deleting <code>{space.key}</code> destroys every page in it, with all
            versions, comments and attachments. It cannot be undone from inside
            Tesria: only a backup taken beforehand would still hold the content.
          </p>
          <button type="button" className="btn btn--danger" onClick={() => setDeleting(true)}>
            Delete This Space
          </button>
        </section>
      )}

      {deleting && (
        <DeleteSpaceDialog
          space={space}
          onCancel={() => setDeleting(false)}
          onDeleted={() => navigate('/spaces', {
            replace: true,
            state: { notice: `The space ${space.key} was deleted.` },
          })}
        />
      )}
      {dialog}
    </>
  )
}
