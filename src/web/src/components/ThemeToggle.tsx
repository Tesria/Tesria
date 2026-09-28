import { useEffect, useState } from 'react'
import { useDismissable } from '../hooks/useDismissable'
import { usePopoverMotion } from './popoverMotion'
import {
  ACCENTS, accentLock, applyAccent, applyPreference, applyReduceMotion, applyStyle, hasBrandAccent, readAccent,
  readPreference, readReduceMotion, readStyle, saveAccent, savePreference, saveReduceMotion, saveStyle, STYLES, systemTheme, themeLock,
  THEME_LABELS, THEME_ORDER, type AccentName, type StylePreference, type ThemePreference,
} from '../theme'
import { useInstance } from '../InstanceContext'

/* Local icons, matching NotificationBell's convention (app chrome defines its
   own rather than importing the editor toolbar's set): 24x24, 1.8px stroke,
   currentColor. */

function Icon({ children, size = 19 }: { children: React.ReactNode; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
         strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

function SunIcon({ size }: { size?: number }) {
  return (
    <Icon size={size}>
      <circle cx="12" cy="12" r="4.25" />
      <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.2 5.2l1.4 1.4M17.4 17.4l1.4 1.4M18.8 5.2l-1.4 1.4M6.6 17.4l-1.4 1.4" />
    </Icon>
  )
}

function MoonIcon({ size }: { size?: number }) {
  return (
    <Icon size={size}>
      <path d="M20 13.5A8.5 8.5 0 1 1 10.5 4a6.75 6.75 0 0 0 9.5 9.5Z" />
    </Icon>
  )
}

function SystemIcon({ size }: { size?: number }) {
  return (
    <Icon size={size}>
      <rect x="2.75" y="4" width="18.5" height="13" rx="2" />
      <path d="M9 20.5h6M12 17v3.5" />
    </Icon>
  )
}

function CheckIcon() {
  return (
    <Icon size={15}>
      <path d="m5 12.5 4.5 4.5L19 7.5" />
    </Icon>
  )
}

const MODE_ICONS: Record<ThemePreference, (p: { size?: number }) => React.ReactElement> = {
  system: SystemIcon,
  light: SunIcon,
  dark: MoonIcon,
}

const MODE_HINTS: Record<ThemePreference, string> = {
  system: 'Follows your operating system',
  light: 'Always light',
  dark: 'Always dark',
}

/**
 * Appearance menu: theme mode (system / light / dark), style (flat or glass,
 * 0.8.1) and accent color.
 *
 * `system` is the default and stays first: a new user gets whatever their OS
 * already asks for, and choosing it again clears the stored preference rather
 * than pinning today's resolved value.
 *
 * While on `system` the trigger icon shows what the OS currently resolves to
 * and re-renders when that flips, so it never sits stale after the user
 * changes their OS theme with this page open.
 */
export function ThemeToggle() {
  const instance = useInstance()
  const [open, setOpen] = useState(false)
  const [preference, setPreference] = useState<ThemePreference>(readPreference)
  const [accent, setAccent] = useState<AccentName>(readAccent)
  const [style, setStyle] = useState<StylePreference>(readStyle)
  const [reduceMotion, setReduceMotion] = useState<boolean>(readReduceMotion)
  const [resolvedSystem, setResolvedSystem] = useState<'light' | 'dark'>(systemTheme)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const panel = usePopoverMotion<HTMLDivElement>(open)

  // index.html already applied both before first paint; these keep the DOM in
  // step after a change, and re-assert after a hot reload.
  useEffect(() => { applyPreference(preference) }, [preference])
  useEffect(() => {
    applyAccent(accent)
  }, [accent])
  useEffect(() => { applyStyle(style) }, [style])
  useEffect(() => { applyReduceMotion(reduceMotion) }, [reduceMotion])

  useEffect(() => {
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => setResolvedSystem(query.matches ? 'dark' : 'light')
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  function chooseMode(next: ThemePreference) {
    setPreference(next)
    savePreference(next)
  }

  function toggleReduceMotion() {
    const next = !reduceMotion
    setReduceMotion(next)
    saveReduceMotion(next)
  }

  function chooseStyle(next: StylePreference) {
    setStyle(next)
    saveStyle(next)
  }

  function chooseAccent(next: AccentName) {
    setAccent(next)
    saveAccent(next)
  }

  // The instance may hold everyone to a theme or an accent (dev-plan 13.1).
  // What is locked is not offered, and with both locked there is nothing to
  // choose, so there is no menu.
  const themeLocked = themeLock() !== null
  const accentLocked = accentLock() !== null
  // The brand's own color comes first, under the brand's name.
  const swatches: { name: AccentName; label: string }[] = hasBrandAccent()
    ? [{ name: 'brand', label: instance?.branding.name ?? 'Brand' }, ...ACCENTS]
    : ACCENTS

  const showing = preference === 'system' ? resolvedSystem : preference
  const TriggerIcon = preference === 'system' ? SystemIcon : showing === 'dark' ? MoonIcon : SunIcon
  const triggerLabel =
    preference === 'system'
      ? `Appearance: system (currently ${resolvedSystem})`
      : `Appearance: ${preference}`

  // The style is always a personal choice, so the menu stays even with the
  // theme and accent both locked.

  return (
    <div className="theme-menu" ref={ref}>
      <button
        type="button"
        className={open ? 'theme-toggle is-open' : 'theme-toggle'}
        onClick={() => setOpen((v) => !v)}
        title={triggerLabel}
        aria-label={triggerLabel}
        aria-haspopup="true"
        aria-expanded={open}
      >
        <TriggerIcon />
      </button>
      {panel.shown && (
        <div className="theme-menu__panel" role="dialog" aria-label="Appearance" ref={panel.ref}>
          <button type="button" className="popover__close" aria-label="Close" onClick={() => setOpen(false)}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </button>
          {!themeLocked && (<>
          <p className="theme-menu__heading">Theme</p>
          <div className="theme-menu__modes">
            {THEME_ORDER.map((mode) => {
              const ModeIcon = MODE_ICONS[mode]
              const active = preference === mode
              return (
                <button
                  key={mode}
                  type="button"
                  className={active ? 'theme-menu__mode is-active' : 'theme-menu__mode'}
                  onClick={() => chooseMode(mode)}
                  aria-pressed={active}
                >
                  <ModeIcon size={17} />
                  <span className="theme-menu__mode-text">
                    <span className="theme-menu__mode-name">
                      {mode === 'system' ? 'System' : THEME_LABELS[mode].replace(' theme', '')}
                    </span>
                    <span className="theme-menu__mode-hint">
                      {mode === 'system' ? `${MODE_HINTS.system} (${resolvedSystem})` : MODE_HINTS[mode]}
                    </span>
                  </span>
                  {active && <span className="theme-menu__check"><CheckIcon /></span>}
                </button>
              )
            })}
          </div>
          </>)}

          <p className="theme-menu__heading">Style</p>
          <div className="theme-menu__modes">
            {STYLES.map((s) => {
              const active = style === s.name
              return (
                <button
                  key={s.name}
                  type="button"
                  className={active ? 'theme-menu__mode is-active' : 'theme-menu__mode'}
                  onClick={() => chooseStyle(s.name)}
                  aria-pressed={active}
                >
                  <span className="theme-menu__mode-text">
                    <span className="theme-menu__mode-name">{s.label}</span>
                    <span className="theme-menu__mode-hint">{s.hint}</span>
                  </span>
                  {active && <span className="theme-menu__check"><CheckIcon /></span>}
                </button>
              )
            })}
            {/* Only glass moves, so only glass has the switch. */}
            {style === 'glass' && (
              <button
                type="button"
                role="switch"
                aria-checked={reduceMotion}
                className="theme-menu__switch"
                onClick={toggleReduceMotion}
              >
                <span className="theme-menu__mode-text">
                  <span className="theme-menu__mode-name">Reduce Motion</span>
                  <span className="theme-menu__mode-hint">No animations when panels open and close</span>
                </span>
                <span className={reduceMotion ? 'switch is-on' : 'switch'} aria-hidden="true"><span className="switch__knob" /></span>
              </button>
            )}
          </div>

          {!accentLocked && (<>
          <p className="theme-menu__heading">Accent Color</p>
          <div className="theme-menu__accents">
            {swatches.map((a) => (
              <button
                key={a.name}
                type="button"
                className={accent === a.name ? 'theme-menu__accent is-active' : 'theme-menu__accent'}
                // The dot reads a themed token, so each swatch previews the
                // color that accent actually produces in the current theme.
                style={{ background: `var(--accent-dot-${a.name})` }}
                onClick={() => chooseAccent(a.name)}
                title={a.label}
                aria-label={a.label}
                aria-pressed={accent === a.name}
              />
            ))}
          </div>
          </>)}
        </div>
      )}
    </div>
  )
}
