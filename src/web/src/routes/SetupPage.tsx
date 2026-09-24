import { type FormEvent, type ReactNode, useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError, type SetupStatus } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { useInstance } from '../InstanceContext'
import { PasswordInput } from '../components/PasswordInput'
import { RecoveryCodes } from '../components/RecoveryCodes'
import { TotpSection } from '../components/TotpSection'
import { AdminRolesPage } from './admin/AdminRolesPage'
import { AuthBrand } from '../components/Brand'

/**
 * First-run setup (dev-plan 10.2).
 *
 * One page with the steps down the left and one step's form on the right, so
 * progress is visible and a finished step can be gone back to. Every step
 * saves through the endpoint that already owns its setting; `/api/setup` only
 * records that a step was answered, and the server decides separately whether
 * the instance is actually set up, from evidence rather than from clicks.
 */

type StepKey =
  | 'welcome' | 'account' | 'instance' | 'registration' | 'permissions'
  | 'backups' | 'email' | 'two-factor' | 'first-space' | 'done'

type Step = { key: StepKey; title: string; blurb: string; required?: boolean }

const STEPS: Step[] = [
  { key: 'welcome', title: 'Welcome', blurb: 'What this covers' },
  { key: 'account', title: 'Your account', blurb: 'The owner of this instance', required: true },
  { key: 'instance', title: 'This instance', blurb: 'Name and address', required: true },
  { key: 'registration', title: 'Who can join', blurb: 'And who can read', required: true },
  { key: 'permissions', title: 'What roles may do', blurb: 'Review the defaults', required: true },
  { key: 'backups', title: 'Backups', blurb: 'How much history to keep', required: true },
  { key: 'email', title: 'Email', blurb: 'Optional' },
  { key: 'two-factor', title: 'Two-factor', blurb: 'Recommended' },
  { key: 'first-space', title: 'A first space', blurb: 'Optional' },
  { key: 'done', title: 'Done', blurb: '' },
]

export function SetupPage() {
  const { user, refresh, register } = useAuth()
  const instance = useInstance()
  const navigate = useNavigate()
  const [at, setAt] = useState<StepKey>('welcome')
  const [status, setStatus] = useState<SetupStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const loadStatus = useCallback(async () => {
    try { setStatus(await api.setup.status()) } catch { /* not the owner yet */ }
  }, [])

  useEffect(() => { if (user) void loadStatus() }, [user, loadStatus])

  // An owner already exists and it is not this visitor: the wizard is over.
  if (instance && !instance.needsOwner && !user) {
    return (
      <div className="setup setup--notice">
        <h1>This instance already has an owner</h1>
        <p className="muted">Setup was finished by whoever created the first account.</p>
        <Link className="btn btn--primary" to="/login">Sign in</Link>
      </div>
    )
  }
  if (user && !user.setupRequired) {
    return (
      <div className="setup setup--notice">
        <h1>Setup is already finished</h1>
        <p className="muted">Everything here lives in Administration now.</p>
        <Link className="btn btn--primary" to="/spaces">Go to the wiki</Link>
      </div>
    )
  }

  // Welcome is never recorded (there is nothing to record), so it counts as
  // done once the wizard has moved past it.
  const done = (key: StepKey) =>
    status?.steps?.[key] != null || (key === 'welcome' && at !== 'welcome')
  const skipped = (key: StepKey) => status?.steps?.[key]?.skipped === true
  const index = STEPS.findIndex((s) => s.key === at)

  async function record(key: StepKey, asSkipped = false) {
    setStatus(await api.setup.recordStep(key, asSkipped))
  }

  /** Records the step and moves on, surfacing whatever the server objects to. */
  async function advance(key: StepKey, save?: () => Promise<unknown>, asSkipped = false) {
    setBusy(true)
    setError(null)
    try {
      if (save) await save()
      if (key !== 'welcome') await record(key, asSkipped)
      const next = STEPS[index + 1]
      if (next) setAt(next.key)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not save.')
    } finally {
      setBusy(false)
    }
  }

  async function finish() {
    setBusy(true)
    setError(null)
    try {
      await api.setup.complete()
      await refresh()
      navigate('/spaces', { replace: true, state: { notice: 'Your instance is ready.' } })
    } catch (err) {
      // 409 names the step still outstanding; go there rather than just
      // saying no.
      const step = err instanceof ApiError ? err.details.step : undefined
      if (typeof step === 'string' && STEPS.some((s) => s.key === step)) {
        setAt(step as StepKey)
        setError('That step still needs finishing.')
      } else {
        setError(err instanceof ApiError ? err.message : 'Could not finish setup.')
      }
      setBusy(false)
    }
  }

  const reachable = (s: Step, i: number) =>
    i <= index || done(s.key) || (s.key === 'account' && !!user)

  return (
    <div className="setup">
      <aside className="setup__rail">
        <AuthBrand />
        <h1 className="setup__brand">Set up {instance?.instanceName ?? 'Tesria'}</h1>
        <ol>
          {STEPS.map((s, i) => (
            <li key={s.key}>
              <button
                type="button"
                className={`setup__step${s.key === at ? ' is-current' : ''}${done(s.key) ? ' is-done' : ''}`}
                disabled={!reachable(s, i)}
                onClick={() => setAt(s.key)}
              >
                <span className="setup__step-mark" aria-hidden="true">
                  {done(s.key) ? (skipped(s.key) ? '–' : '✓') : i + 1}
                </span>
                <span>
                  <strong>{s.title}</strong>
                  <span className="muted small">
                    {s.required ? 'Required' : s.blurb}
                  </span>
                </span>
              </button>
            </li>
          ))}
        </ol>
      </aside>

      <main className="setup__panel">
        {error && <p className="alert alert--error">{error}</p>}

        {at === 'welcome' && (
          <Panel title="Welcome" onNext={() => advance('welcome')} busy={busy} nextLabel="Start">
            <p>
              This sets up the instance you have just installed: who owns it, who
              can join, what each role may do, and how much history the backups
              keep.
            </p>
            <p>
              The five required steps take about two minutes. Email,
              two-factor and a first space can wait, and everything here can be
              changed later in Administration.
            </p>
          </Panel>
        )}

        {at === 'account' && (
          <AccountStep
            hasAccount={!!user}
            busy={busy}
            onRegister={async (email, name, password) => {
              setBusy(true); setError(null)
              try { return await register(email, name, password) } finally { setBusy(false) }
            }}
            onDone={() => advance('account', () => api.auth.acknowledgeRecoveryCodes())}
          />
        )}

        {at === 'instance' && (
          <InstanceStep busy={busy} onNext={(name, baseUrl) =>
            advance('instance', () => api.admin.settings.update({ instanceName: name, baseUrl }))} />
        )}

        {at === 'registration' && (
          <RegistrationStep busy={busy} onNext={(open, anonymous) =>
            advance('registration', () => api.admin.settings.update({
              allowPublicRegistration: open,
              allowPublicSpaces: anonymous,
            }))} />
        )}

        {at === 'permissions' && (
          <Panel
            title="What roles may do"
            onNext={() => advance('permissions', () => api.admin.roles.review())}
            busy={busy}
            nextLabel="Keep these defaults"
          >
            <p className="muted">
              A tier decides who may act on whom; a role decides what they may
              do. Change anything now or leave it: you are the owner, so every
              column here is yours to edit, and Administration keeps this page.
            </p>
            <div className="setup__embed">
              <AdminRolesPage />
            </div>
          </Panel>
        )}

        {at === 'backups' && (
          <BackupsStep busy={busy} onNext={(policy) =>
            advance('backups', () => api.admin.backups.savePolicy(policy))} />
        )}

        {at === 'email' && (
          <EmailStep
            busy={busy}
            onSkip={() => advance('email', undefined, true)}
            onNext={(input) => advance('email', () => api.admin.settings.update(input))}
          />
        )}

        {at === 'two-factor' && (
          <Panel
            title="Two-factor"
            onNext={() => advance('two-factor')}
            onSkip={() => advance('two-factor', undefined, true)}
            busy={busy}
          >
            <p className="muted">
              Recommended for the account that owns the instance: it is the one
              account nobody else can reset.
            </p>
            <TotpSection />
          </Panel>
        )}

        {at === 'first-space' && (
          <FirstSpaceStep
            busy={busy}
            onSkip={() => advance('first-space', undefined, true)}
            onNext={(key, name) => advance('first-space', async () => { await api.spaces.create({ key, name }) })}
          />
        )}

        {at === 'done' && (
          <DoneStep
            busy={busy}
            skippedKeys={STEPS.filter((s) => skipped(s.key)).map((s) => s.title)}
            onFinish={finish}
          />
        )}
      </main>
    </div>
  )
}

/** The shell every step shares: a heading, the form, and the way onward. */
function Panel({
  title, children, onNext, onSkip, busy, nextLabel = 'Continue', nextDisabled = false, asForm = false,
}: {
  title: string
  children: ReactNode
  onNext?: () => void
  onSkip?: () => void
  busy: boolean
  nextLabel?: string
  nextDisabled?: boolean
  asForm?: boolean
}) {
  const actions = (
    <div className="row-gap setup__actions">
      {onNext && (
        <button
          type={asForm ? 'submit' : 'button'}
          className="btn btn--primary"
          disabled={busy || nextDisabled}
          onClick={asForm ? undefined : onNext}
        >
          {busy ? 'Saving…' : nextLabel}
        </button>
      )}
      {onSkip && (
        <button type="button" className="btn btn--ghost" disabled={busy} onClick={onSkip}>
          Skip for now
        </button>
      )}
    </div>
  )
  return (
    <section className="setup__step-panel">
      <h2>{title}</h2>
      {children}
      {actions}
    </section>
  )
}

function AccountStep({
  hasAccount, busy, onRegister, onDone,
}: {
  hasAccount: boolean
  busy: boolean
  onRegister: (email: string, name: string, password: string) => Promise<string[]>
  onDone: () => void
}) {
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [password, setPassword] = useState('')
  const [codes, setCodes] = useState<string[] | null>(null)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try { setCodes(await onRegister(email, name, password)) } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the account.')
    }
  }

  // Already signed in but the codes were never acknowledged: offer the
  // checkbox on its own rather than a second registration form.
  if (hasAccount && !codes) {
    return (
      <Panel title="Your account" busy={busy} onNext={onDone} nextLabel="Continue">
        <p className="muted">
          This account owns the instance. If you have not saved your recovery
          codes, do that from your profile before going on.
        </p>
      </Panel>
    )
  }

  if (codes) {
    return (
      <Panel title="Save your recovery codes" busy={busy} onNext={onDone} nextDisabled={!saved}>
        <p className="muted">
          These are the only way back in if you lose your password. Each works
          once. The owner cannot be reset by anyone else, so losing these and
          the password means losing the instance.
        </p>
        <RecoveryCodes codes={codes} />
        <label className="setup__check">
          <input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
          I have saved these somewhere safe
        </label>
      </Panel>
    )
  }

  return (
    <form className="setup__step-panel" onSubmit={submit}>
      <h2>Your account</h2>
      <p className="muted">
        The first account becomes the owner: the one account that can transfer
        the instance, and the one nobody else can suspend or reset.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      <label>
        <span>Email</span>
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
      </label>
      <label>
        <span>Your name</span>
        <input value={name} onChange={(e) => setName(e.target.value)} required />
      </label>
      <label>
        <span>Password</span>
        <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password" required minLength={8} />
      </label>
      <div className="row-gap setup__actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create the owner account'}
        </button>
      </div>
    </form>
  )
}

function InstanceStep({ busy, onNext }: { busy: boolean; onNext: (name: string, baseUrl: string) => void }) {
  const instance = useInstance()
  const [name, setName] = useState(instance?.instanceName ?? 'Tesria')
  const [baseUrl, setBaseUrl] = useState(window.location.origin)
  return (
    <form className="setup__step-panel" onSubmit={(e) => { e.preventDefault(); onNext(name, baseUrl) }}>
      <h2>This instance</h2>
      <label>
        <span>What is it called</span>
        <input value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
      </label>
      <label>
        <span>Its address</span>
        <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} required />
        <span className="muted small">Links in email use this, so it has to be the address people reach you on.</span>
      </label>
      <div className="row-gap setup__actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Saving…' : 'Continue'}
        </button>
      </div>
    </form>
  )
}

function RegistrationStep({ busy, onNext }: { busy: boolean; onNext: (open: boolean, anonymous: boolean) => void }) {
  const [open, setOpen] = useState<boolean | null>(null)
  const [anonymous, setAnonymous] = useState(false)
  return (
    <Panel
      title="Who can join"
      busy={busy}
      nextDisabled={open === null}
      onNext={() => open !== null && onNext(open, anonymous)}
    >
      <div className="setup__cards">
        <button type="button" className={`setup__card${open === false ? ' is-chosen' : ''}`}
          onClick={() => setOpen(false)}>
          <strong>Invite only</strong>
          <span className="muted small">You create invite links. Nobody can sign up on their own.</span>
        </button>
        <button type="button" className={`setup__card${open === true ? ' is-chosen' : ''}`}
          onClick={() => setOpen(true)}>
          <strong>Open</strong>
          <span className="muted small">Anyone who can reach this address can create an account.</span>
        </button>
      </div>
      <label className="setup__check">
        <input type="checkbox" checked={anonymous} onChange={(e) => setAnonymous(e.target.checked)} />
        <span>
          <strong>Allow anonymous reading</strong>
          <span className="muted small">
            Off: every visitor must sign in. On: spaces you mark public can be read
            without an account, and nothing is public until you mark a space.
          </span>
        </span>
      </label>
    </Panel>
  )
}

function BackupsStep({
  busy, onNext,
}: {
  busy: boolean
  onNext: (policy: { enabled: boolean; keepCount: number; keepDays: number }) => void
}) {
  const [enabled, setEnabled] = useState(true)
  const [keepCount, setKeepCount] = useState(3)
  const [keepDays, setKeepDays] = useState(14)
  return (
    <Panel
      title="Backups"
      busy={busy}
      nextLabel="Keep these settings"
      onNext={() => onNext({ enabled, keepCount, keepDays })}
    >
      <p className="muted">
        Two systems run already: a nightly dump of the database and uploads, and
        a continuous physical backup you can rewind to any moment. This decides
        how much of that history is kept.
      </p>
      <p className="alert alert--error small">
        <strong>BACKUP_ENCRYPTION_KEY</strong> in your <code>.env</code> is what
        decrypts those backups. Keep a copy somewhere that is not this machine,
        or a backup you can reach is a backup you cannot read.
      </p>
      <label className="setup__check">
        <input type="checkbox" checked={!enabled} onChange={(e) => setEnabled(!e.target.checked)} />
        Keep every backup forever
      </label>
      {enabled && (
        <p className="setup__inline">
          Keep the newest
          <input type="number" min={1} value={keepCount}
            onChange={(e) => setKeepCount(Number(e.target.value))} />
          backups, and everything from the last
          <input type="number" min={1} value={keepDays}
            onChange={(e) => setKeepDays(Number(e.target.value))} />
          days.
        </p>
      )}
    </Panel>
  )
}

function EmailStep({
  busy, onNext, onSkip,
}: {
  busy: boolean
  onNext: (input: { smtpHost: string; smtpPort: number; smtpUsername: string; smtpFromAddress: string; emailEnabled: boolean }) => void
  onSkip: () => void
}) {
  const [host, setHost] = useState('')
  const [port, setPort] = useState(587)
  const [username, setUsername] = useState('')
  const [from, setFrom] = useState('')
  return (
    <Panel
      title="Email"
      busy={busy}
      onSkip={onSkip}
      nextDisabled={!host || !from}
      onNext={() => onNext({
        smtpHost: host, smtpPort: port, smtpUsername: username,
        smtpFromAddress: from, emailEnabled: true,
      })}
    >
      <p className="muted">
        Used for password recovery, invitations and notifications. Without it,
        people who forget a password need you to reset it for them. You can
        finish this later in Administration.
      </p>
      <label><span>SMTP host</span><input value={host} onChange={(e) => setHost(e.target.value)} /></label>
      <label><span>Port</span><input type="number" value={port} onChange={(e) => setPort(Number(e.target.value))} /></label>
      <label><span>Username</span><input value={username} onChange={(e) => setUsername(e.target.value)} /></label>
      <label><span>From address</span><input type="email" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
      <p className="muted small">
        The password and a test send are on Administration &rarr; Settings, which
        has the whole form.
      </p>
    </Panel>
  )
}

function FirstSpaceStep({
  busy, onNext, onSkip,
}: {
  busy: boolean
  onNext: (key: string, name: string) => void
  onSkip: () => void
}) {
  const [name, setName] = useState('')
  const [key, setKey] = useState('')
  // The key follows the name until someone types in it. It used to fill in
  // only while empty, so after the first letter of the name it stuck: "Team
  // handbook" gave the key T (found writing the Support site, 2026-09-24).
  const [keyEdited, setKeyEdited] = useState(false)
  return (
    <Panel
      title="A first space"
      busy={busy}
      onSkip={onSkip}
      nextDisabled={!name || key.length < 2}
      onNext={() => onNext(key.toUpperCase(), name)}
    >
      <p className="muted">
        A space holds pages. Most instances start with one and grow: a team, a
        project, a handbook.
      </p>
      <label>
        <span>Name</span>
        <input value={name} autoFocus onChange={(e) => {
          setName(e.target.value)
          // A key starts with a letter (the server's rule), so leading digits are dropped.
          if (!keyEdited) setKey(e.target.value.replace(/[^a-zA-Z0-9]/g, '').replace(/^[0-9]+/, '').slice(0, 6).toUpperCase())
        }} />
      </label>
      <label>
        <span>Key</span>
        <input value={key} onChange={(e) => { setKeyEdited(true); setKey(e.target.value.toUpperCase()) }} />
        <span className="muted small">Part of every page address in it, and it cannot change later.</span>
      </label>
    </Panel>
  )
}

function DoneStep({
  busy, skippedKeys, onFinish,
}: {
  busy: boolean
  skippedKeys: string[]
  onFinish: () => void
}) {
  return (
    <section className="setup__step-panel">
      <h2>Your instance is ready</h2>
      <p className="muted">
        Everything here lives in Administration now, including whatever you
        skipped.
      </p>
      {skippedKeys.length > 0 && (
        <p className="muted small">
          Left for later: {skippedKeys.join(', ')}. Administration has each of them.
        </p>
      )}
      <picture className="setup__shot">
        <source srcSet="/onboarding/admin-overview.dark.png" media="(prefers-color-scheme: dark)" />
        <img src="/onboarding/admin-overview.light.png" alt="The Administration dashboard" />
      </picture>
      <div className="row-gap setup__actions">
        <button type="button" className="btn btn--primary" disabled={busy} onClick={onFinish}>
          {busy ? 'Finishing…' : 'Finish'}
        </button>
        <Link className="btn btn--ghost" to="/admin/invites">Invite people</Link>
      </div>
    </section>
  )
}
