import type { Swatch } from './palette'

/**
 * The swatch grid itself, with no opinion about what it colours: the
 * highlight dropdown and the table cell-background menu both render it and
 * differ only in which tiers they pass and what `onPick` does.
 *
 * Every button uses onMouseDown-preventDefault, the same guard
 * ToolbarButton.tsx uses: without it, pressing a swatch blurs the editor and
 * drops the very selection the colour is about to be applied to.
 */
export function ColorPalette({
  tiers,
  current,
  onPick,
  onClear,
  clearLabel,
}: {
  tiers: Swatch[][]
  /** Currently applied value, if any: shown as a ring on the matching swatch. */
  current?: string | null
  onPick: (value: string) => void
  onClear: () => void
  clearLabel: string
}) {
  const normalized = current?.toLowerCase() ?? null
  return (
    <div className="swatches">
      {tiers.map((tier, i) => (
        <div className="swatches__row" key={i}>
          {tier.map((s) => (
            <button
              key={s.value}
              type="button"
              className={normalized === s.value ? 'swatch is-active' : 'swatch'}
              style={{ background: s.css ?? s.value }}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => onPick(s.value)}
              title={s.name}
              aria-label={s.name}
            />
          ))}
        </div>
      ))}
      <button
        type="button"
        className="swatches__clear"
        onMouseDown={(e) => e.preventDefault()}
        onClick={onClear}
      >
        {clearLabel}
      </button>
    </div>
  )
}
