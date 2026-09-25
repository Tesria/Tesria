// Makes tesria-deploy.zip (dev-plan 14.2): everything needed to run a
// release of Tesria from its published images, and nothing else. No source,
// no Dockerfiles, no Git: the compose file, the example settings, and the
// files the running stack reads from its own folder (the Caddy
// configuration, the database, backup and pgBackRest scripts, Tailscale's
// settings, and the helper scripts people run).
//
//   node scripts/bundle/deploy-bundle.mjs 0.7.0 [out-dir]
//
// The compose file is changed in two ways: its build instructions are
// removed, so `docker compose up` can only run the published images, and
// the image version defaults to this release, so a download of 0.7.0 runs
// 0.7.0 even if `latest` has moved on. Files are at the top of the zip, so
// `unzip tesria-deploy.zip -d tesria` gives a ready folder, and unzipping a
// newer one over it upgrades everything except .env, which it never holds.

import { execFileSync } from 'node:child_process'
import { chmodSync, cpSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const version = process.argv[2]
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version ?? '')) {
  console.error('usage: node scripts/bundle/deploy-bundle.mjs <version, such as 0.7.0> [out-dir]')
  process.exit(2)
}
const OUT = resolve(process.argv[3] ?? ROOT)

/** What the running stack reads, and what people run. Dockerfiles are left out. */
const FILES = [
  '.env.example',
  'LICENSE',
  'NOTICE',
  'deploy/Caddyfile',
  'deploy/Caddyfile.public',
  'deploy/db/entrypoint.sh',
  'deploy/pgbackrest',
  'deploy/backup',
  'deploy/tailscale/serve.json',
  'deploy/scripts',
  'deploy/docker-desktop',
]
const SKIP = /(^|\/)Dockerfile$/

/** The compose file for images only: no build instructions, and this release's version by default. */
export function composeForRelease(text, version) {
  const lines = text.split('\n')
  const out = []
  for (let i = 0; i < lines.length; i++) {
    const build = lines[i].match(/^(\s*)build:\s*$/)
    if (build) {
      // Skip the block: every following line indented deeper than `build:`.
      const indent = build[1].length
      while (i + 1 < lines.length && (lines[i + 1].trim() === '' ? false : lines[i + 1].match(/^\s*/)[0].length > indent)) i++
      continue
    }
    out.push(lines[i])
  }
  const result = out.join('\n').replaceAll('${TESRIA_IMAGE_TAG:-latest}', `\${TESRIA_IMAGE_TAG:-${version}}`)
  if (/^\s*build:/m.test(result)) throw new Error('a build section survived')
  if (!result.includes(`TESRIA_IMAGE_TAG:-${version}`)) throw new Error('no image version was pinned')
  return `# Tesria ${version}: the compose file for its published images (tesria-deploy.zip).\n`
    + `# Built from docker-compose.yml by scripts/bundle/deploy-bundle.mjs; to build from\n`
    + `# source instead, clone https://github.com/Tesria/Tesria.\n`
    + result
}

const README = (v) => `Tesria ${v}
${'='.repeat(`Tesria ${v}`.length)}

Everything needed to run Tesria ${v} from its published images.
The full guide is at https://tesria.com/docs (Getting started, Quick start).

First time:

  1. Copy .env.example to .env and fill in POSTGRES_PASSWORD, APP_DB_PASSWORD,
     BACKUP_ENCRYPTION_KEY (each a long random value) and DOMAIN.
  2. docker compose pull
  3. docker compose up -d
  4. Open https://<DOMAIN> and the setup wizard takes it from there.

Upgrading: unzip the newer tesria-deploy.zip over this folder (your .env is
kept: it is not in the zip), then run steps 2 and 3. Read the release notes
first: https://tesria.com/docs (Release notes).

Never run "docker compose down -v": the -v deletes the wiki and its backups.

Source code, issues and releases: https://github.com/Tesria/Tesria
License: Apache 2.0 (LICENSE, NOTICE).
`

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const stage = join(tmpdir(), `tesria-deploy-${process.pid}`)
  rmSync(stage, { recursive: true, force: true })
  mkdirSync(stage, { recursive: true })

  for (const f of FILES) {
    const from = join(ROOT, f)
    if (!existsSync(from)) throw new Error(`missing: ${f}`)
    // cpSync keeps each file's mode, so the scripts stay executable.
    cpSync(from, join(stage, f), { recursive: true, filter: (src) => !SKIP.test(src) })
  }
  writeFileSync(join(stage, 'docker-compose.yml'),
    composeForRelease(readFileSync(join(ROOT, 'docker-compose.yml'), 'utf8'), version))
  writeFileSync(join(stage, 'README.txt'), README(version))
  // The shell scripts run inside containers and on the host; make sure they
  // are executable whatever the checkout did with modes.
  for (const script of execFileSync('find', [stage, '-name', '*.sh'], { encoding: 'utf8' }).split('\n').filter(Boolean))
    chmodSync(script, statSync(script).mode | 0o755)

  mkdirSync(OUT, { recursive: true })
  const zip = join(OUT, 'tesria-deploy.zip')
  rmSync(zip, { force: true })
  execFileSync('zip', ['-qrX', zip, '.'], { cwd: stage })
  rmSync(stage, { recursive: true, force: true })
  console.log(`${zip}: Tesria ${version}, ${(statSync(zip).size / 1024).toFixed(0)} KB`)
}
