/**
 * An alert's details in words (T7-015). The Security tab listed the
 * detector's metadata as it was stored: "Failures: 20 · WindowMinutes: 10",
 * "LastAction: page.trashed", "DistinctAccounts: 5". Each known field is
 * written as a phrase here, the counts read together with their window
 * ("20 failed sign-ins in 10 minutes"), and a field this does not know yet
 * at least gets its name in words rather than in code.
 */

type Meta = Record<string, unknown>

/** Fields shown elsewhere (the denied-requests breakdown) or not worth a line (ids). */
const SKIP = new Set([
  'TopPaths', 'Browsers', 'SharedAddress', 'Unauthorized', 'Forbidden', 'WithSession', 'WithToken', 'Anonymous',
  'JobId', 'WindowMinutes',
])

/** A count read together with the alert's window. */
const COUNTED: Record<string, [one: string, many: string]> = {
  Failures: ['failed sign-in', 'failed sign-ins'],
  DistinctAccounts: ['account tried', 'different accounts tried'],
  Lockouts: ['lockout', 'lockouts'],
  Registrations: ['new account', 'new accounts'],
  Pages: ['page removed', 'pages removed'],
  Tokens: ['API token made', 'API tokens made'],
  Denied: ['refused request', 'refused requests'],
}

const ACTIONS: Record<string, string> = {
  'page.trashed': 'moved to the trash',
  'page.purged': 'deleted for good',
  'page.deleted': 'deleted',
}

/** "10 minutes", "an hour", "2 hours". */
export function minutesWords(minutes: number): string {
  if (minutes === 60) return 'an hour'
  if (minutes > 60 && minutes % 60 === 0) return `${minutes / 60} hours`
  return minutes === 1 ? 'a minute' : `${minutes} minutes`
}

function count(n: number, [one, many]: [string, string]) {
  return `${n} ${n === 1 ? one : many}`
}

function when(value: unknown): string {
  const d = new Date(String(value))
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleString()
}

function bytes(value: unknown): string {
  const n = Number(value)
  if (!Number.isFinite(n)) return String(value)
  const gb = n / 1024 ** 3
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${Math.round(n / 1024 ** 2)} MB`
}

/** "LastWalAt" → "Last wal at": better than the identifier, for a field nobody named yet. */
export function humanize(key: string): string {
  const words = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** Each field as a phrase, in the order the detector wrote them. */
export function alertDetails(meta: Meta): string[] {
  const window = typeof meta.WindowMinutes === 'number' ? meta.WindowMinutes : null
  const out: string[] = []
  for (const [key, value] of Object.entries(meta)) {
    if (SKIP.has(key) || value === null || value === undefined || value === '') continue
    if (typeof value === 'object' && !Array.isArray(value)) continue
    const counted = COUNTED[key]
    if (counted && typeof value === 'number') {
      out.push(window ? `${count(value, counted)} in ${minutesWords(window)}` : count(value, counted))
      continue
    }
    switch (key) {
      case 'LastAction': out.push(`Last: ${ACTIONS[String(value)] ?? String(value)}`); break
      case 'FailedLoginCount': out.push(count(Number(value), COUNTED.Failures)); break
      case 'LockedUntil': out.push(`Locked until ${when(value)}`); break
      case 'Email': out.push(`Account: ${String(value)}`); break
      case 'KnownAddresses': out.push(`${value} ${value === 1 ? 'address' : 'addresses'} seen before`); break
      case 'Key': out.push(`Space: ${String(value)}`); break
      case 'Name': out.push(`Name: ${String(value)}`); break
      case 'Enabled': out.push(value ? 'Turned on' : 'Turned off'); break
      case 'Url': out.push(`Address: ${String(value)}`); break
      case 'BrokenAtSequence': out.push(`Broken at entry ${String(value)}`); break
      case 'Checked': out.push(`${value} entries checked`); break
      case 'Agent': out.push(`Backup: ${String(value)}`); break
      case 'Slot': out.push(`Offsite copy: ${String(value)}`); break
      case 'Trigger': out.push(`Started by: ${String(value)}`); break
      case 'IntervalHours': out.push(`Runs every ${minutesWords(Number(value) * 60)}`); break
      case 'VolumeFreeBytes':
        out.push(meta.VolumeTotalBytes ? `${bytes(value)} free of ${bytes(meta.VolumeTotalBytes)}` : `${bytes(value)} free`)
        break
      case 'VolumeTotalBytes': if (meta.VolumeFreeBytes == null) out.push(`${bytes(value)} in all`); break
      case 'Backlog': out.push(`${value} changes waiting`); break
      case 'Settings':
      case 'Configured':
        out.push(Array.isArray(value) ? value.join(', ') : String(value)); break
      case 'From': out.push(`From: ${String(value)}`); break
      case 'To': out.push(`To: ${String(value)}`); break
      default:
        if (/At$/.test(key)) out.push(`${humanize(key.replace(/At$/, ''))}: ${when(value)}`)
        else out.push(`${humanize(key)}: ${Array.isArray(value) ? value.join(', ') : String(value)}`)
    }
  }
  return out
}
