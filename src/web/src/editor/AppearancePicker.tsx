import { APPEARANCES, APPEARANCE_LABELS, isAppearance } from './appearance'

/**
 * The "Style" choice on a status, chart, diagram or code block (0.8.1):
 * Theme default, Minimal or Glass. A plain select, like the chart's Type.
 */
export function AppearancePicker({
  value, onChange, className,
}: {
  value: unknown
  onChange: (next: string) => void
  className?: string
}) {
  const current = isAppearance(value) ? value : 'theme'
  return (
    <label className={className ? `appearance-picker ${className}` : 'appearance-picker'} title="This block's style: follow the reader's theme, or always flat, or always glass">
      <span>Style</span>
      <select value={current} onChange={(e) => onChange(e.target.value)}>
        {APPEARANCES.map((a) => <option key={a} value={a}>{APPEARANCE_LABELS[a]}</option>)}
      </select>
    </label>
  )
}
