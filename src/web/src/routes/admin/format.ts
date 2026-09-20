/** Small formatters shared by the admin pages. */

export function bytes(value: number): string {
  if (value <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/** "3 hours ago", "in 20 minutes". Coarse on purpose: this is for status, not audit. */
export function relative(iso: string, now = Date.now(), style: Intl.RelativeTimeFormatStyle = 'long'): string {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000)
  const abs = Math.abs(seconds)
  const [value, unit]: [number, Intl.RelativeTimeFormatUnit] =
    abs < 60 ? [seconds, 'second']
      : abs < 3600 ? [Math.round(seconds / 60), 'minute']
        : abs < 86400 ? [Math.round(seconds / 3600), 'hour']
          : [Math.round(seconds / 86400), 'day']
  return new Intl.RelativeTimeFormat(undefined, { numeric: 'auto', style }).format(value, unit)
}

export function duration(fromIso: string | null, toIso: string | null): string {
  if (!fromIso || !toIso) return ''
  const s = Math.max(0, Math.round((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 1000))
  if (s < 60) return `${s}s`
  if (s < 3600) return `${Math.floor(s / 60)}m ${s % 60}s`
  return `${Math.floor(s / 3600)}h ${Math.floor((s % 3600) / 60)}m`
}
