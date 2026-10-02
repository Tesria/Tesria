import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError, GlobalGroupId, LIMITS, SpaceGroupRole, type Directory } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Wizard, type WizardStep } from './Wizard'
import { PeoplePicker } from './PeoplePicker'
import {
  type Audience, audienceSentence, everyoneAccessOf, groupMembers, keyProblem, LEVEL_WORDS, suggestKey,
} from '../routes/spaceWizard'

const STEPS: WizardStep[] = [
  { key: 'name', title: 'Name and Key', blurb: 'What it is called' },
  { key: 'access', title: 'Who Can See It', blurb: 'Everyone, or its groups' },
  { key: 'people', title: 'Who Is in Its Groups', blurb: 'Optional' },
  { key: 'review', title: 'Review and Create', blurb: '' },
]

/** Each of a space's groups, in the order the Permissions tab shows them, with what it may do. */
const GROUPS: { role: number; name: string; what: string }[] = [
  { role: SpaceGroupRole.Admins, name: 'Admins', what: 'Can view, edit and manage the space, and see past page restrictions.' },
  { role: SpaceGroupRole.Editors, name: 'Editors', what: 'Can view and edit.' },
  { role: SpaceGroupRole.Viewers, name: 'Viewers', what: 'Can view.' },
  { role: SpaceGroupRole.Reviewers, name: 'Reviewers', what: 'Can view for now. Reviewing changes arrives with review mode.' },
]

/**
 * Creating a space (dev-plan 21.2): its name and key, who may see it, and
 * who goes in each of its four groups, then one create call that makes all
 * of it together. Replaces the plain form; the setup wizard's own first
 * space step still asks only for a name and key.
 */
export function NewSpaceWizard({ onCancel }: { onCancel: () => void }) {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [at, setAt] = useState('name')
  const [reached, setReached] = useState(0)
  const [name, setName] = useState('')
  const [key, setKey] = useState('')
  // The key follows the name until someone types in it (as setup's first space does).
  const [keyEdited, setKeyEdited] = useState(false)
  const [description, setDescription] = useState('')
  const [audience, setAudience] = useState<Audience>({ kind: 'everyone', level: 1 })
  const [chosen, setChosen] = useState<Record<number, string[]>>({})
  const [people, setPeople] = useState<Directory[]>([])
  const [globalReaders, setGlobalReaders] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.users.list().then(setPeople).catch(() => {})
    // Whether Global Viewers or Global Reviewers has anyone, to say so on step 2.
    api.groups.list()
      .then((groups) => setGlobalReaders(groups.some((g) =>
        (g.id === GlobalGroupId.Viewers || g.id === GlobalGroupId.Reviewers) && g.memberCount > 0)))
      .catch(() => {})
  }, [])

  const index = STEPS.findIndex((s) => s.key === at)
  const problem = keyProblem(key)
  const nameOk = name.trim().length > 0
  const firstOk = nameOk && problem === null
  const me = user?.id ?? ''
  const members = groupMembers(chosen, me)
  const nameOf = (id: string) => people.find((p) => p.id === id)?.displayName ?? 'Someone'

  function go(next: string) {
    const i = STEPS.findIndex((s) => s.key === next)
    setReached((r) => Math.max(r, i))
    setAt(next)
    setError(null)
  }

  async function create() {
    setBusy(true)
    setError(null)
    try {
      const space = await api.spaces.create({
        key,
        name: name.trim(),
        description: description.trim() || null,
        everyoneAccess: everyoneAccessOf(audience),
        members: members.filter((m) => m.userIds.length > 0),
      })
      navigate(`/spaces/${space.key}`)
    } catch (err) {
      // A key someone else has, or a name or key the server refuses, is fixed on the first step.
      if (err instanceof ApiError && (err.status === 409 || err.fieldErrors.key || err.fieldErrors.name)) setAt('name')
      setError(err instanceof ApiError ? err.message : 'Could not create the space.')
      setBusy(false)
    }
  }

  const next = STEPS[index + 1]
  const actions = (
    <>
      {at === 'review'
        ? <button type="button" className="btn btn--primary" disabled={busy || !firstOk} onClick={() => void create()}>
            {busy ? 'Creating…' : 'Create Space'}
          </button>
        : <button type="button" className="btn btn--primary" disabled={at === 'name' && !firstOk}
            onClick={() => next && go(next.key)}>Continue</button>}
      {index > 0 && <button type="button" className="btn" disabled={busy} onClick={() => go(STEPS[index - 1].key)}>Back</button>}
      <button type="button" className="btn btn--ghost" disabled={busy} onClick={onCancel}>Cancel</button>
    </>
  )

  return (
    <Wizard
      title="New Space"
      steps={STEPS}
      at={at}
      onGo={go}
      reachable={(_, i) => firstOk && i <= reached}
      done={(k) => STEPS.findIndex((s) => s.key === k) < reached || (k === 'name' && firstOk && at !== 'name')}
      actions={actions}
    >
      {error && <p className="alert alert--error">{error}</p>}

      {at === 'name' && (
        <>
          <label>
            <span>Name</span>
            <input value={name} autoFocus maxLength={LIMITS.spaceName} placeholder="Engineering" onChange={(e) => {
              setName(e.target.value)
              if (!keyEdited) setKey(suggestKey(e.target.value))
            }} />
          </label>
          <label>
            <span>Key</span>
            <input value={key} maxLength={LIMITS.spaceKey} placeholder="ENG" aria-invalid={key !== '' && problem !== null}
              onChange={(e) => { setKeyEdited(true); setKey(e.target.value.toUpperCase()) }} />
            <span className={`small${key !== '' && problem ? ' wizard__problem' : ' muted'}`}>
              {key !== '' && problem ? problem : 'Part of every page address in it, and it cannot change later.'}
            </span>
          </label>
          <label>
            <span>Description (Optional)</span>
            <input value={description} maxLength={LIMITS.spaceDescription} onChange={(e) => setDescription(e.target.value)} />
          </label>
          <p className="muted small">An icon can be chosen afterwards, in the space’s settings.</p>
        </>
      )}

      {at === 'access' && (
        <>
          <div className="setup__cards">
            <button type="button" aria-pressed={audience.kind === 'everyone'}
              className={`setup__card${audience.kind === 'everyone' ? ' is-chosen' : ''}`}
              onClick={() => setAudience(audience.kind === 'everyone' ? audience : { kind: 'everyone', level: 1 })}>
              <strong>Everyone Signed In</strong>
              <span className="muted small">Everyone with an account here, at the level you choose below.</span>
            </button>
            <button type="button" aria-pressed={audience.kind === 'groups'}
              className={`setup__card${audience.kind === 'groups' ? ' is-chosen' : ''}`}
              onClick={() => setAudience({ kind: 'groups' })}>
              <strong>Only the People in Its Groups</strong>
              <span className="muted small">You choose them in the next step, and can change them any time.</span>
            </button>
          </div>
          {audience.kind === 'everyone' && (
            <label>
              <span>Everyone signed in</span>
              <span className="glass-select-wrap"><select className="glass-select" value={audience.level}
                onChange={(e) => setAudience({ kind: 'everyone', level: Number(e.target.value) as 0 | 1 | 2 })}>
                {([0, 1, 2] as const).map((l) => <option key={l} value={l}>{LEVEL_WORDS[l]}</option>)}
              </select></span>
            </label>
          )}
          <p>{audienceSentence(audience)}</p>
          <p className="muted small">
            Either way, a page restricted to particular people stays restricted.
            {globalReaders && ' Global Viewers and Global Reviewers can read every space, this one included.'}
            {' '}This can be changed later in the space’s Permissions.
          </p>
        </>
      )}

      {at === 'people' && (
        <>
          <p className="muted">
            Each group gives its people that access to this space. You are in Admins already. Leave a group
            empty if nobody needs it yet.
          </p>
          {GROUPS.map((g) => (
            <fieldset key={g.role} className="wizard__group">
              <legend>{name.trim() || 'This space'} {g.name}</legend>
              <p className="muted small">{g.what}</p>
              <PeoplePicker
                // The creator is an admin already, which includes every other group's access.
                people={g.role === SpaceGroupRole.Admins ? people : people.filter((p) => p.id !== me)}
                label={`Add people to ${g.name}`}
                fixed={g.role === SpaceGroupRole.Admins && me ? [me] : []}
                chosen={(chosen[g.role] ?? []).filter((id) => id !== me || g.role !== SpaceGroupRole.Admins)}
                onChange={(ids) => setChosen((c) => ({ ...c, [g.role]: ids }))}
              />
            </fieldset>
          ))}
        </>
      )}

      {at === 'review' && (
        <dl className="wizard__summary">
          <div className="wizard__summary-row">
            <dt>Name</dt>
            <dd>{name.trim()} <span className="muted">({key})</span></dd>
          </div>
          {description.trim() && (
            <div className="wizard__summary-row"><dt>Description</dt><dd>{description.trim()}</dd></div>
          )}
          <div className="wizard__summary-row">
            <dt>Who can see it</dt>
            <dd>
              {audience.kind === 'everyone'
                ? `Everyone signed in: ${LEVEL_WORDS[audience.level].toLowerCase()}`
                : 'Only the people in its groups'}
            </dd>
          </div>
          {GROUPS.map((g) => {
            const ids = g.role === SpaceGroupRole.Admins
              ? [me, ...(members.find((m) => m.role === g.role)?.userIds ?? [])]
              : members.find((m) => m.role === g.role)?.userIds ?? []
            return (
              <div key={g.role} className="wizard__summary-row">
                <dt>{g.name}</dt>
                <dd>
                  {ids.length === 0
                    ? <span className="muted">Nobody yet</span>
                    : ids.map((id) => (id === me ? 'You' : nameOf(id))).join(', ')}
                </dd>
              </div>
            )
          })}
        </dl>
      )}
    </Wizard>
  )
}
