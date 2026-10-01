import { type FormEvent, type ReactNode, useCallback, useEffect, useId, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError, MailSignIn, type BackupKeyStatus, type MailProvider, type SetupStatus } from '../api/client'
import { MailProviderHint, MailProviderPicker } from '../components/MailProviderPicker'
import { useAuth } from '../auth/AuthContext'
import { useInstance } from '../InstanceContext'
import { PasswordInput } from '../components/PasswordInput'
import { PASSWORD_HINT, PASSWORD_MAX, PASSWORD_MIN, passwordProblem } from '../auth/passwordRule'
import { RecoveryCodes } from '../components/RecoveryCodes'
import { TotpSection } from '../components/TotpSection'
import { AdminRolesPage } from './admin/AdminRolesPage'
import { AuthBrand } from '../components/Brand'
import { BackupKeyWhere } from './admin/BackupKeyNotice'

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
  { key: 'account', title: 'Your Account', blurb: 'The owner of this instance', required: true },
  { key: 'instance', title: 'This Instance', blurb: 'Name and address', required: true },
  { key: 'registration', title: 'Who Can Join', blurb: 'And who can read', required: true },
  { key: 'permissions', title: 'What Roles May Do', blurb: 'Review the defaults', required: true },
  { key: 'backups', title: 'Backups', blurb: 'How much history to keep', required: true },
  { key: 'email', title: 'Email', blurb: 'Optional' },
  { key: 'two-factor', title: 'Two-Factor', blurb: 'Recommended' },
  { key: 'first-space', title: 'A First Space', blurb: 'Optional' },
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
        <h1>This Instance Already Has an Owner</h1>
        <p className="muted">Setup was finished by whoever created the first account.</p>
        <Link className="btn btn--primary" to="/login">Sign In</Link>
      </div>
    )
  }
  if (user && !user.setupRequired) {
    return (
      <div className="setup setup--notice">
        <h1>Setup Is Already Finished</h1>
        <p className="muted">Everything here lives in Administration now.</p>
        <Link className="btn btn--primary" to="/spaces">Go to the Wiki</Link>
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
        <h1 className="setup__brand">Set Up {instance?.instanceName ?? 'Tesria'}</h1>
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
            advance('instance', async () => {
              // The address as it came, the one Tesria works out for itself
              // (DOMAIN and TESRIA_HTTPS_PORT), is left blank rather than
              // saved, so it keeps following them if they change (R-006, the
              // 0.8.3 Windows retest). A different one is the owner's.
              const current = await api.admin.settings.get()
              const plain = (u: string) => u.trim().replace(/\/+$/, '').toLowerCase()
              const deployed = !current.baseUrl && plain(baseUrl) === plain(current.effectiveBaseUrl)
              return api.admin.settings.update({ instanceName: name, baseUrl: deployed ? '' : baseUrl })
            })} />
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
            title="What Roles May Do"
            onNext={() => advance('permissions', () => api.admin.roles.review())}
            busy={busy}
            nextLabel="Keep These Defaults"
          >
            <p className="muted">
              A tier decides who may act on whom; a role decides what they may
              do. Change anything now or leave it: you are the owner, so every
              column here is yours to edit, and Administration keeps this page.
            </p>
            <div className="setup__embed">
              <AdminRolesPage inSetup />
            </div>
          </Panel>
        )}

        {at === 'backups' && (
          <BackupsStep busy={busy} onNext={(policy, keySaved) =>
            advance('backups', async () => {
              await api.admin.backups.savePolicy(policy)
              if (keySaved) await api.admin.backups.keySaved()
            })} />
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
            title="Two-Factor"
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
          Skip for Now
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
  const hintId = useId()

  async function submit(e: FormEvent) {
    e.preventDefault()
    const weak = passwordProblem(password)
    setError(weak)
    if (weak) return
    try { setCodes(await onRegister(email, name, password)) } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the account.')
    }
  }

  // Already signed in but the codes were never acknowledged: offer the
  // checkbox on its own rather than a second registration form.
  if (hasAccount && !codes) {
    return (
      <Panel title="Your Account" busy={busy} onNext={onDone} nextLabel="Continue">
        <p className="muted">
          This account owns the instance. If you have not saved your recovery
          codes, do that from your profile before going on.
        </p>
      </Panel>
    )
  }

  if (codes) {
    return (
      <Panel title="Save Your Recovery Codes" busy={busy} onNext={onDone} nextDisabled={!saved}>
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
      <h2>Your Account</h2>
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
        <span>Your Name</span>
        <input value={name} onChange={(e) => setName(e.target.value)} required />
      </label>
      <label>
        <span>Password</span>
        <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password" required minLength={PASSWORD_MIN} maxLength={PASSWORD_MAX} describedBy={hintId} />
        <span className="muted small" id={hintId}>{PASSWORD_HINT}</span>
      </label>
      <div className="row-gap setup__actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create the Owner Account'}
        </button>
      </div>
    </form>
  )
}

function InstanceStep({ busy, onNext }: { busy: boolean; onNext: (name: string, baseUrl: string) => void }) {
  const instance = useInstance()
  const [name, setName] = useState(instance?.instanceName ?? 'Tesria')
  const [baseUrl, setBaseUrl] = useState(window.location.origin)
  // A Public Address already saved is what the box starts from, so passing
  // this step again (resuming the wizard) keeps it rather than putting the
  // browser's address over it (t1-R01, the 0.8.3 retest).
  useEffect(() => {
    let active = true
    api.admin.settings.get()
      .then((s) => { if (active && s.baseUrl) setBaseUrl(s.baseUrl) })
      .catch(() => { /* the browser's address stays */ })
    return () => { active = false }
  }, [])
  return (
    <form className="setup__step-panel" onSubmit={(e) => { e.preventDefault(); onNext(name, baseUrl) }}>
      <h2>This Instance</h2>
      <label>
        <span>What Is It Called</span>
        <input value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
      </label>
      <label>
        <span>Its Address</span>
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
      title="Who Can Join"
      busy={busy}
      nextDisabled={open === null}
      onNext={() => open !== null && onNext(open, anonymous)}
    >
      <div className="setup__cards">
        <button type="button" className={`setup__card${open === false ? ' is-chosen' : ''}`}
          onClick={() => setOpen(false)}>
          <strong>Invite Only</strong>
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
          <strong>Allow Anonymous Reading</strong>
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
  onNext: (policy: { enabled: boolean; keepCount: number; keepDays: number }, keySaved: boolean) => void
}) {
  const [enabled, setEnabled] = useState(true)
  const [keepCount, setKeepCount] = useState(3)
  const [keepDays, setKeepDays] = useState(14)
  // The backup key (dev-plan 25.1). Asked about only when Tesria generated
  // it; a key set in .env was chosen by someone who has it.
  const [key, setKey] = useState<BackupKeyStatus | null>(null)
  const [keyChoice, setKeyChoice] = useState<'saved' | 'later' | null>(null)
  useEffect(() => {
    api.admin.backups.overview().then((o) => setKey(o.key)).catch(() => undefined)
  }, [])
  const askAboutKey = key?.generated === true && !key.savedAt

  return (
    <Panel
      title="Backups"
      busy={busy}
      nextLabel="Keep These Settings"
      nextDisabled={askAboutKey && keyChoice === null}
      onNext={() => onNext({ enabled, keepCount, keepDays }, askAboutKey && keyChoice === 'saved')}
    >
      <p className="muted">
        Two systems run already: a nightly dump of the database and uploads, and
        a continuous physical backup you can rewind to any moment. This decides
        how much of that history is kept.
      </p>
      {askAboutKey ? (
        <div className="setup__key">
          <h3>Save Your Backup Key</h3>
          <p className="small">
            Your backups are encrypted with a key Tesria made when it was installed. Without it, no
            backup can be restored, by anyone. Right now it may exist only on the server, so save a copy
            somewhere else, such as a password manager.
          </p>
          <p className="small"><strong>Where to find it</strong>, on the server:</p>
          <BackupKeyWhere />
          <label className="setup__check">
            <input type="radio" name="backup-key" checked={keyChoice === 'saved'}
              onChange={() => setKeyChoice('saved')} />
            I saved it somewhere that is not the server
          </label>
          <label className="setup__check">
            <input type="radio" name="backup-key" checked={keyChoice === 'later'}
              onChange={() => setKeyChoice('later')} />
            Someone else runs the server; they will save it
          </label>
          {keyChoice === 'later' && (
            <p className="muted small">
              Administration, Backups will keep asking until someone says it is saved. The key
              belongs with whoever runs the server, and the steps above work for them too.
            </p>
          )}
        </div>
      ) : key && !key.generated ? (
        <p className="alert alert--error small">
          <strong>BACKUP_ENCRYPTION_KEY</strong> in your <code>.env</code> is what
          decrypts those backups. Keep a copy somewhere that is not this machine,
          or a backup you can reach is a backup you cannot read.
        </p>
      ) : null}
      <label className="setup__check">
        <input type="checkbox" checked={!enabled} onChange={(e) => setEnabled(!e.target.checked)} />
        Keep Every Backup Forever
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
  onNext: (input: {
    smtpProvider: string; smtpHost: string; smtpPort: number; smtpTls: number; smtpUsername: string
    smtpPassword?: string; smtpFromAddress: string; emailEnabled: boolean
  }) => void
  onSkip: () => void
}) {
  const [providers, setProviders] = useState<MailProvider[]>([])
  const [providerId, setProviderId] = useState('')
  const [host, setHost] = useState('')
  const [port, setPort] = useState(587)
  const [tls, setTls] = useState(1)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [from, setFrom] = useState('')
  useEffect(() => {
    api.admin.settings.mailProviders().then(setProviders).catch(() => setProviders([]))
  }, [])
  const provider = providers.find((p) => p.id === providerId) ?? null
  return (
    <Panel
      title="Email"
      busy={busy}
      onSkip={onSkip}
      nextDisabled={!host || !from}
      onNext={() => onNext({
        smtpProvider: providerId, smtpHost: host, smtpPort: port, smtpTls: tls, smtpUsername: username,
        ...(password ? { smtpPassword: password } : {}),
        smtpFromAddress: from, emailEnabled: true,
      })}
    >
      <p className="muted">
        Used for password recovery, invitations and notifications. Without it,
        people who forget a password need you to reset it for them. You can
        finish this later in Administration.
      </p>
      <MailProviderPicker
        providers={providers}
        value={providerId}
        disabled={busy}
        onChange={(p) => {
          setProviderId(p?.id ?? '')
          if (p) { setHost(p.host); setPort(p.port); setTls(p.tls) }
        }}
      />
      {provider && <MailProviderHint provider={provider} />}
      {provider?.signIn != null && (
        <p className="muted small">
          To sign in with {provider.signIn === MailSignIn.Microsoft ? 'Microsoft' : 'Google'} instead of a
          password, skip this step and use Administration &rarr; Settings &rarr; Email once the wizard is done.
        </p>
      )}
      <label><span>SMTP Host</span><input value={host} onChange={(e) => setHost(e.target.value)} /></label>
      <label><span>Port</span><input type="number" value={port} onChange={(e) => setPort(Number(e.target.value))} /></label>
      <label>
        <span>Encryption</span>
        <select value={tls} onChange={(e) => setTls(Number(e.target.value))}>
          <option value={0}>None</option>
          <option value={1}>STARTTLS</option>
          <option value={2}>SSL on Connect</option>
        </select>
      </label>
      <label><span>Username</span><input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" /></label>
      <label>
        <span>Password</span>
        <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="off" />
      </label>
      <label><span>From Address</span><input type="email" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
      <p className="muted small">
        A test send is on Administration &rarr; Settings, once the wizard is done.
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
  // handbook" gave the key T (found writing the docs, 2026-09-24).
  const [keyEdited, setKeyEdited] = useState(false)
  return (
    <Panel
      title="A First Space"
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
      <h2>Your Instance Is Ready</h2>
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
        <Link className="btn btn--ghost" to="/admin/invites">Invite People</Link>
      </div>
    </section>
  )
}
