// Screenshots of the first-run setup wizard for the Docs space's
// "First-run setup wizard" page (dev-plan 10.5 step 6). Run it through
// shoot-setup.sh, which explains the two phases.
//
// The wizard only exists on an instance with no owner, so it is shot on the
// scratch instance (scripts/scratch-instance.sh), never the real one. The
// pictures land beside the Getting started section's other shots, where
// publish-docs.mjs finds them by name.

import { spawnSync } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const ROOT = join(HERE, '..', '..')
const DIR = join(HERE, 'shots', 'getting-started')

const phase = process.argv[2]
if (phase !== 'before' && phase !== 'after') {
  console.error('usage: shoot-setup.sh before|after')
  process.exit(2)
}

const primary = '.setup__step-panel .setup__actions .btn--primary'
const skip = '.setup__step-panel .setup__actions button:has-text("Skip for now")'
const rail = (title) => ({ click: `.setup__step:has-text("${title}")` })
const settle = { wait: 900 }

// The address field starts as the address the browser used, which here is
// the inside of a container; a reader should see an address like theirs.
const address = { type: 'https://wiki.example.com', selector: '.setup__step-panel label:has-text("Its address") input' }
const NO_REVIEWER = "document.querySelectorAll('p').forEach((el) => { if (el.textContent.includes('Last reviewed')) el.textContent = el.textContent.replace(/\\s*Last reviewed[^.]*\\./, '') })"
const inviteOnly = { click: '.setup__cards .setup__card >> nth=0' }

/**
 * Each shot as [name, the steps that reach it on a desktop, on a phone].
 *
 * The desktop run goes through the wizard the way a person does, one step's
 * button at a time, which records each step on the scratch instance. The
 * phone run comes second and finds those steps finished, so it jumps to each
 * one from the list of steps instead. Nothing clicks Finish: the wizard stays
 * open, so either phase can be run again.
 */
const before = [
  ['setup-welcome', [], []],
  ['setup-account', [{ click: primary }, settle,
    { type: 'alex@example.com', selector: '.setup__step-panel input[type="email"]' },
    { type: 'Alex Rivera', selector: '.setup__step-panel label:has-text("Your name") input' },
    { type: 'not-a-real-password', selector: '.setup__step-panel input[autocomplete="new-password"]' }]],
]
const after = [
  // Start, then Continue past the account the owner has already made.
  ['setup-instance', [{ click: primary }, settle, { click: primary }, settle, address], [rail('This instance'), settle, address]],
  ['setup-registration', [{ click: primary }, settle, inviteOnly], [rail('Who can join'), settle, inviteOnly]],
  ['setup-permissions', [{ click: primary }, { wait: 2000 }], [rail('What roles may do'), { wait: 2000 }]],
  ['setup-backups', [{ click: primary }, settle], [rail('Backups'), settle]],
  ['setup-email', [{ click: primary }, settle], [rail('Email'), settle]],
  ['setup-two-factor', [{ click: skip }, { wait: 1500 }], [rail('Two-factor'), { wait: 1500 }]],
  ['setup-first-space', [{ click: skip }, settle], [rail('A first space'), settle]],
  // Done is never recorded as a step, so the list cannot jump to it.
  ['setup-done', [{ click: skip }, settle], [rail('A first space'), settle, { click: skip }, settle]],
]
const list = phase === 'before' ? before : after

const spec = (kind) => ({
  shots: list.map(([name, desktop, phone], i) => ({
    name,
    ...(i === 0 ? { url: '/setup', waitFor: '.setup__step-panel h2' } : {}),
    // A rerun has already kept the defaults once, so the table says when and
    // by whom; a reader on a new instance never sees that.
    steps: [...(kind === 'desktop' ? desktop : (phone ?? desktop)),
      ...(name === 'setup-permissions' ? [{ eval: NO_REVIEWER }] : [])],
    settle: 600,
    // The same window as the section's other whole-window shots. On a phone
    // the list of steps sits above the form: the welcome picture shows it
    // once, whole, and every other phone picture is cut to the step's form,
    // because ten copies of that list made the page 14,000 pixels long.
    // The roles table is cut to the step itself: a whole window made its
    // text small on a phone, where the page shows it at the phone's width.
    ...(kind === 'desktop'
      ? { viewport: { width: 1024, height: 640 }, ...(name === 'setup-permissions' ? { clipTo: '.setup__step-panel', clipPad: 8 } : {}) }
      : { viewport: { width: 390, height: 700 }, ...(name === 'setup-welcome' ? { fullPage: true } : { clipTo: '.setup__panel' }) }),
  })),
})

const signIn = phase === 'after'
  ? { SHOT_EMAIL: process.env.SCRATCH_EMAIL, SHOT_PASSWORD: process.env.SCRATCH_PASSWORD, SHOT_SIGNIN: '' }
  : { SHOT_EMAIL: '', SHOT_PASSWORD: '', SHOT_SIGNIN: '0' }

for (const [kind, mobile] of [['desktop', ''], ['phone', '1']]) {
  mkdirSync(join(DIR, kind), { recursive: true })
  const file = join(DIR, `setup-${phase}.${kind}.json`)
  writeFileSync(file, JSON.stringify(spec(kind), null, 2))
  const run = spawnSync(join(ROOT, 'scripts/screenshots/run.sh'), [file], {
    stdio: 'inherit',
    env: {
      ...process.env, ...signIn,
      SHOT_OUT: join(DIR, kind), SHOT_MOBILE: mobile, SHOT_THEME: 'light', SHOT_ACCENT: 'blue',
      // SCRATCH_CONTAINER picks another copy: a second scratch project keeps
      // an owner-less instance for "before" while the first has its owner.
      SHOT_NETWORK: `container:${process.env.SCRATCH_CONTAINER || 'tesria-scratch-app-1'}`, SHOT_BASE: 'http://localhost:8080',
    },
  })
  if (run.status !== 0) {
    console.error(`the ${kind} screenshots failed`)
    process.exit(1)
  }
}
