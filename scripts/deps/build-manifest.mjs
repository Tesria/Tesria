// Writes the dependency manifest the About tab in Administration shows, and
// the third-party notices it links to (the owner, 2026-09-24: "a full
// dependency list with attribution ... so users can see if there are any
// deps that have active CVEs and they know their exposure").
//
//   node scripts/deps/build-manifest.mjs          write both files
//   node scripts/deps/build-manifest.mjs --check  fail if the manifest is stale
//
// The manifest (src/Api/About/dependencies.json) comes from the lockfiles and
// `dotnet list package`, so CI can check it without installing anything. The
// notices (src/Api/About/THIRD-PARTY-NOTICES.txt) also read each package's
// own license file, which needs node_modules and the NuGet cache present;
// run it after `npm ci` in src/web, collab and pdf, and a restore.
//
// Only what ships is listed: production npm packages (a devDependency builds
// the app but is not in it) and the server's NuGet packages, direct and
// transitive, plus the container base images.

import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..')
const OUT_JSON = join(ROOT, 'src/Api/About/dependencies.json')
const OUT_NOTICES = join(ROOT, 'src/Api/About/THIRD-PARTY-NOTICES.txt')
const check = process.argv.includes('--check')

/** npm workspaces that ship, and what the About tab calls them. */
const NPM = [
  ['src/web', 'Web app'],
  ['collab', 'Collaboration service'],
  ['pdf', 'PDF service'],
]

/**
 * Packages whose lockfile entry names no license, with the license their own
 * license file states (checked by hand; the file is in the notices).
 */
const LICENSE_OVERRIDES = {
  khroma: 'MIT', // node_modules/khroma/license, 2026-09-24
}

const deps = []
const licenseFiles = new Map() // key -> text

function npmPackages(dir, component) {
  const lock = JSON.parse(readFileSync(join(ROOT, dir, 'package-lock.json'), 'utf8'))
  const direct = new Set(Object.keys(lock.packages?.['']?.dependencies ?? {}))
  for (const [path, pkg] of Object.entries(lock.packages ?? {})) {
    if (!path.startsWith('node_modules/') || pkg.dev || pkg.devOptional || pkg.link) continue
    const name = path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length)
    const key = `npm:${name}@${pkg.version}`
    deps.push({
      ecosystem: 'npm',
      name,
      version: pkg.version,
      license: pkg.license ?? LICENSE_OVERRIDES[name] ?? 'UNKNOWN',
      component,
      direct: direct.has(name) && path === `node_modules/${name}`,
      url: `https://www.npmjs.com/package/${name}/v/${pkg.version}`,
    })
    if (!licenseFiles.has(key)) {
      const text = readLicenseIn(join(ROOT, dir, path))
      if (text) licenseFiles.set(key, text)
    }
  }
}

function readLicenseIn(folder) {
  if (!existsSync(folder)) return null
  const file = readdirSync(folder).find((f) => (/^(licen[cs]e|copying|notice)(\.(md|txt|markdown))?$/i.test(f)
    || /^licen[cs]e[-_.]/i.test(f)) && statSync(join(folder, f)).isFile())
  return file ? readFileSync(join(folder, file), 'utf8').trim() : null
}

function nugetPackages() {
  const out = execFileSync('dotnet', ['list', join(ROOT, 'src/Api'), 'package', '--include-transitive', '--format', 'json'],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
  const project = JSON.parse(out).projects[0]
  const fw = project.frameworks[0]
  const cache = process.env.NUGET_PACKAGES ?? join(homedir(), '.nuget', 'packages')
  const add = (p, direct) => {
    const id = p.id
    const version = p.resolvedVersion
    const folder = join(cache, id.toLowerCase(), version.toLowerCase())
    let license = 'UNKNOWN'
    const nuspec = join(folder, `${id.toLowerCase()}.nuspec`)
    if (existsSync(nuspec)) {
      const xml = readFileSync(nuspec, 'utf8')
      const expr = xml.match(/<license\s+type="expression"\s*>([^<]+)<\/license>/i)
      const file = xml.match(/<license\s+type="file"\s*>([^<]+)<\/license>/i)
      const url = xml.match(/<licenseUrl>([^<]+)<\/licenseUrl>/i)
      if (expr) license = expr[1].trim()
      else if (file) license = `See ${file[1].trim()}`
      else if (url) license = `See ${url[1].trim()}`
      const key = `nuget:${id}@${version}`
      const text = file && existsSync(join(folder, file[1].trim()))
        ? readFileSync(join(folder, file[1].trim()), 'utf8').trim()
        : readLicenseIn(folder)
      if (text) licenseFiles.set(key, text)
    }
    deps.push({
      ecosystem: 'NuGet', name: id, version, license, component: 'Server', direct,
      url: `https://www.nuget.org/packages/${id}/${version}`,
    })
  }
  for (const p of fw.topLevelPackages ?? []) add(p, true)
  for (const p of fw.transitivePackages ?? []) add(p, false)
}

/** What each base image is for, shown beside it on the About tab. */
const IMAGE_NOTES = {
  'mcr.microsoft.com/dotnet/aspnet': 'Runs the app: the server and the web pages.',
  'mcr.microsoft.com/dotnet/sdk': 'Builds the app. Not part of what runs.',
  'node': 'Runs the collaboration service, and builds the web app.',
  'postgres': 'Runs the database and both backup services.',
  'mcr.microsoft.com/playwright': 'Runs the PDF service (exports).',
  'caddy': 'Runs Caddy, which answers HTTPS in front of everything.',
  'tailscale/tailscale': 'Optional: only when started with --profile tailscale.',
  'quay.io/minio/minio': 'Testing only: offsite backup tests (--profile offsite-test).',
}

function images() {
  const found = new Map()
  const files = ['deploy/Dockerfile', 'deploy/db/Dockerfile', 'deploy/backup/Dockerfile', 'collab/Dockerfile', 'pdf/Dockerfile']
  for (const f of files) {
    if (!existsSync(join(ROOT, f))) continue
    for (const m of readFileSync(join(ROOT, f), 'utf8').matchAll(/^FROM\s+(\S+)/gim)) found.set(m[1], f)
  }
  const compose = readFileSync(join(ROOT, 'docker-compose.yml'), 'utf8')
  for (const m of compose.matchAll(/^\s+image:\s*(\S+)/gm)) found.set(m[1], 'docker-compose.yml')
  for (const [ref] of found) {
    const at = ref.lastIndexOf(':')
    deps.push({
      ecosystem: 'Container', name: at > 0 ? ref.slice(0, at) : ref, version: at > 0 ? ref.slice(at + 1) : 'latest',
      license: 'See the image', component: 'Container image', direct: true, url: null,
      note: IMAGE_NOTES[at > 0 ? ref.slice(0, at) : ref] ?? null,
    })
  }
}

for (const [dir, component] of NPM) npmPackages(dir, component)
nugetPackages()
images()

// One entry per package, version and component, sorted, so the file only
// changes when a dependency does.
const unique = new Map()
for (const d of deps) unique.set(`${d.component}|${d.ecosystem}|${d.name}|${d.version}`, d)
const list = [...unique.values()].sort((a, b) =>
  a.component.localeCompare(b.component) || a.name.localeCompare(b.name) || a.version.localeCompare(b.version))
const manifest = JSON.stringify({ format: 1, dependencies: list }, null, 2) + '\n'

if (check) {
  const current = existsSync(OUT_JSON) ? readFileSync(OUT_JSON, 'utf8') : ''
  if (current !== manifest) {
    console.error('src/Api/About/dependencies.json is out of date: run node scripts/deps/build-manifest.mjs and commit it.')
    process.exit(1)
  }
  console.log(`dependency manifest current (${list.length} entries)`)
  process.exit(0)
}

writeFileSync(OUT_JSON, manifest)

// The notices: every package's license, with its own license file when the
// package ships one. Grouped by package, not by component, so a package
// used twice is credited once.
const seen = new Set()
const lines = [
  'Third-party software in Tesria',
  '==============================',
  '',
  'Tesria includes the software listed below, each under its own license.',
  'Generated by scripts/deps/build-manifest.mjs from the lockfiles and package',
  'metadata. Container base images are not included here: each image carries',
  'its own notices.',
  '',
]
let withText = 0
for (const d of list) {
  if (d.ecosystem === 'Container') continue
  const key = `${d.ecosystem === 'npm' ? 'npm' : 'nuget'}:${d.name}@${d.version}`
  if (seen.has(key)) continue
  seen.add(key)
  lines.push('-'.repeat(78), `${d.name} ${d.version} (${d.ecosystem})`, `License: ${d.license}`, d.url ?? '', '')
  const text = licenseFiles.get(key)
  if (text) { lines.push(text, ''); withText++ }
}
writeFileSync(OUT_NOTICES, lines.join('\n').replace(/\r\n/g, '\n').trimEnd() + '\n')
console.log(`wrote ${list.length} dependencies; license texts for ${withText} of ${seen.size} packages`)
