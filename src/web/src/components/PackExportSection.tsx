import { api } from '../api/client'
import { ExportProgressView } from './ExportProgress'
import { useExportRun } from './useExportRun'

/**
 * Space settings → Export as a pack (dev-plan 8.5).
 *
 * The sibling of the site export, and the copy has one job: to say which of
 * the two you want. A site is for readers and is finished; a pack is for
 * Tesria and is still editable. Someone who picks the wrong one finds out
 * late, so the difference is said plainly rather than left to the names.
 *
 * There is no audience to choose here. A pack exports what the person asking
 * can see, because anything less would quietly drop pages from a file whose
 * whole purpose is to be the copy that survives.
 */
export function PackExportSection({ spaceKey }: { spaceKey: string }) {
  const exporting = useExportRun(`${spaceKey.toLowerCase()}-pack.zip`, 'The pack could not be built.')
  const run = () => exporting.run((options) => api.spaces.exportPack(spaceKey, options))

  return (
    <>
      <p className="muted small">
        The space itself, as a zip Tesria can read back: pages with their history,
        attachments, comments, labels and templates. Import it into another
        instance, or keep it as the copy that outlives this one. Unlike a site
        export, what comes out is still editable.
      </p>
      <p className="muted small">
        The files inside are readable JSON and the same space always packs to the
        same bytes, so a pack can live in a Git repository and a commit shows what
        actually changed.
      </p>
      {exporting.error && <p className="alert alert--error">{exporting.error}</p>}

      <div className="row-gap" style={{ marginTop: '0.75rem' }}>
        <button type="button" className="btn btn--primary" disabled={exporting.busy} onClick={run}>
          {exporting.busy ? 'Packing…' : 'Export as a pack'}
        </button>
      </div>
      <ExportProgressView state={exporting} noun="pack" />
      <p className="muted small">
        Everything you can read goes in, restricted pages included, so treat the
        file as you would the space. Who may read what does not travel with it:
        an imported space starts private to whoever imported it.
      </p>
    </>
  )
}
