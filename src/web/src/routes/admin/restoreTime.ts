/**
 * The Restore dialog's time field, as plain logic (KI-19).
 *
 * A `datetime-local` input works in whole minutes, and the range it is given
 * comes in seconds. Rounded the easy way, the default (the end of the
 * backup, 01:38:43, shown as 01:38) could sit past the maximum (the last
 * archived WAL, 01:37:41, shown as 01:37), and the browser then refused to
 * submit the form with nothing on the page to say why. So the minimum is
 * rounded up, the maximum down, and the value shown is kept between them.
 */

/** A local date and time in the form a `datetime-local` input takes: 2026-09-29T01:38. */
export function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function floorMinute(iso: string): Date {
  const d = new Date(iso)
  d.setSeconds(0, 0)
  return d
}

function ceilMinute(iso: string): Date {
  const d = floorMinute(iso)
  if (d.getTime() < new Date(iso).getTime()) d.setMinutes(d.getMinutes() + 1)
  return d
}

export interface TimeRange {
  /** The earliest whole minute inside the range, for the input's `min`. */
  min?: string
  /** The latest whole minute inside the range, for the input's `max`. */
  max?: string
  /** What the field shows before anyone edits it: the target, kept inside the range. */
  initial: string
}

export function restoreTimeRange(
  earliest: string | null | undefined,
  latest: string | null | undefined,
  target: string | null | undefined,
): TimeRange {
  const min = earliest ? toLocalInput(ceilMinute(earliest)) : undefined
  const max = latest ? toLocalInput(floorMinute(latest)) : undefined
  let initial = target ? toLocalInput(floorMinute(target)) : ''
  // The same fixed-width format throughout, so text order is time order.
  if (initial && max && initial > max) initial = max
  if (initial && min && initial < min) initial = min
  return { min, max, initial }
}

/** Whether a value typed into the field lies outside the range; an empty field does not. */
export function outsideRange(value: string, range: TimeRange): boolean {
  if (!value) return false
  return (range.min !== undefined && value < range.min) || (range.max !== undefined && value > range.max)
}
