import { type ReactNode, useEffect, useId, useRef, useState } from 'react'

export type WizardStep = { key: string; title: string; blurb?: string }

/**
 * The shell of a short wizard inside the app (dev-plan 21.2 and 21.3), in
 * the setup wizard's look: the steps down the side on a computer, one step
 * at a time beside them, and on a phone the list folded into one line,
 * "Step 2 of 4: Who Can See It", that opens it (as SetupPage does since QA
 * T9-022). The steps say nothing about saving: the caller decides what each
 * one asks and when the whole is sent.
 */
export function Wizard({
  title, steps, at, onGo, reachable, done, children, actions,
}: {
  title: string
  steps: WizardStep[]
  at: string
  onGo: (key: string) => void
  /** Whether a step may be opened from the list, for one not reached yet. */
  reachable: (key: string, index: number) => boolean
  done: (key: string) => boolean
  children: ReactNode
  /** The way onward and back, under the step. */
  actions: ReactNode
}) {
  const index = Math.max(0, steps.findIndex((s) => s.key === at))
  // Open only on the step it was opened on, so moving on folds it again.
  const [openAt, setOpenAt] = useState<string | null>(null)
  const open = openAt === at
  const listId = useId()
  const headingRef = useRef<HTMLHeadingElement>(null)
  const first = useRef(true)

  // A new step starts with its heading in view, and focus on it for a
  // screen reader; not on the first render, which would pull the page down.
  useEffect(() => {
    if (first.current) { first.current = false; return }
    headingRef.current?.focus({ preventScroll: true })
    headingRef.current?.scrollIntoView({ block: 'nearest' })
  }, [at])

  return (
    <div className="wizard">
      <aside className="wizard__rail">
        <p className="wizard__title">{title}</p>
        <button type="button" className="setup__progress" aria-expanded={open} aria-controls={listId}
          onClick={() => setOpenAt(open ? null : at)}>
          <span>Step {index + 1} of {steps.length}: <strong>{steps[index]?.title}</strong></span>
          <span className="setup__progress-chevron" aria-hidden="true" />
        </button>
        <ol id={listId} className={open ? 'is-open' : undefined}>
          {steps.map((s, i) => (
            <li key={s.key}>
              <button
                type="button"
                className={`setup__step${s.key === at ? ' is-current' : ''}${done(s.key) ? ' is-done' : ''}`}
                aria-current={s.key === at ? 'step' : undefined}
                disabled={s.key !== at && !reachable(s.key, i)}
                onClick={() => { onGo(s.key); setOpenAt(null) }}
              >
                <span className="setup__step-mark" aria-hidden="true">{done(s.key) && s.key !== at ? '✓' : i + 1}</span>
                <span>
                  <strong>{s.title}</strong>
                  {s.blurb && <span className="muted small">{s.blurb}</span>}
                </span>
              </button>
            </li>
          ))}
        </ol>
      </aside>
      <section className="wizard__panel setup__step-panel">
        <h2 ref={headingRef} tabIndex={-1}>{steps[index]?.title}</h2>
        {children}
        <div className="row-gap setup__actions">{actions}</div>
      </section>
    </div>
  )
}
