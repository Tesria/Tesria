import { PieChart } from '../../components/PieChart'
import type { DiskChart } from '../../api/client'

/**
 * Administration → Backups → Disk space (dev-plan 9.3).
 *
 * Backups against everything else against free, per disk. The numbers sit
 * beside the chart rather than inside it, because the numbers are the point:
 * a pie answers "roughly how much" at a glance and nothing more precisely
 * than that, and the question people actually bring to this page is whether
 * the disk is about to fill up.
 */

/**
 * What to call the disk. It is measured through the host's own filesystem,
 * so on a Mac or Windows box it is the machine's disk rather than anything
 * Docker owns, and "virtiofs0" would mean nothing to the person reading it.
 * A real device name is kept, because on a server that is the useful detail.
 */
function diskName(filesystem: string | null): string {
  if (!filesystem || /^virtiofs|^osxfs|^grpcfuse/i.test(filesystem)) return 'This machine'
  return filesystem
}

function bytes(n: number): string {
  if (n === 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(n) / Math.log(1024)), units.length - 1)
  return `${(n / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

function percent(part: number, whole: number): string {
  if (whole <= 0) return ''
  const p = (part / whole) * 100
  return p > 0 && p < 1 ? '<1%' : `${Math.round(p)}%`
}

function relative(iso: string | null): string {
  if (!iso) return 'not yet'
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 90) return 'just now'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} minutes ago`
  const hours = Math.round(minutes / 60)
  return hours < 24 ? `${hours} hours ago` : `${Math.round(hours / 24)} days ago`
}

function DiskCard({ disk }: { disk: DiskChart }) {
  // Four separate hues rather than shades of one. Grey read as "disabled"
  // rather than as a slice, and two blues for the wiki and its backups were
  // taken for the same thing.
  const slices = [
    { label: 'The wiki', value: disk.wikiBytes, color: 'var(--chart-wiki)' },
    { label: 'Its backups', value: disk.backupBytes, color: 'var(--chart-backups)' },
    { label: 'Everything else', value: disk.otherBytes, color: 'var(--chart-other)' },
    { label: 'Free', value: disk.freeBytes, color: 'var(--chart-free)' },
  ]

  return (
    <section className="backup-card">
      <h3 className="backup-card__title">{diskName(disk.filesystem)}</h3>
      <div className="disk-chart">
        <PieChart
          slices={slices}
          size={104}
          label={`The wiki ${bytes(disk.wikiBytes)}, its backups ${bytes(disk.backupBytes)}, everything else ${bytes(disk.otherBytes)}, free ${bytes(disk.freeBytes)} of ${bytes(disk.totalBytes)}`}
        />
        <ul className="disk-chart__legend">
          {slices.map((s) => (
            <li key={s.label} className="disk-chart__row">
              <span className="disk-chart__name">
                <span className="disk-chart__key" style={{ background: s.color }} aria-hidden="true" />
                {s.label}
              </span>
              <span className="disk-chart__value">
                {bytes(s.value)} · {percent(s.value, disk.totalBytes)}
              </span>
            </li>
          ))}
        </ul>
      </div>

      {disk.low && (
        <p className="alert alert--warning small">
          <strong>Free space is running out.</strong> There is less room left than two more
          backups would take. Backups fail when the disk fills, so either free some space or
          tighten the retention policy below.
        </p>
      )}

      <p className="muted small">
        {bytes(disk.totalBytes)} in total · measured {relative(disk.measuredAt)}
      </p>
    </section>
  )
}

export function DiskSpace({ disks }: { disks: DiskChart[] }) {
  if (disks.length === 0) return null

  return (
    <section className="profile__section profile__section--wide" id="disk-space">
      <h2>Disk space</h2>
      <p className="muted small">
        What this machine&rsquo;s disk is holding: the live wiki, the backups of it, and
        everything else on the same disk. Measured through the host&rsquo;s own filesystem,
        so the free space here is the free space your computer reports, not the size
        Docker&rsquo;s virtual disk claims it could grow to.
      </p>
      <div className="backup-cards">
        {disks.map((d) => <DiskCard key={d.filesystem ?? d.agents.join()} disk={d} />)}
      </div>
    </section>
  )
}
