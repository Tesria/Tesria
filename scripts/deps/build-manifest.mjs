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

/**
 * The standard texts in scripts/deps/licenses, for a package that ships no
 * license file of its own (the review's LIC-01, 2026-09-24: 54 packages were
 * listed with a name and a URL, which does not carry their conditions). Each
 * is verbatim from a copy on hand: MIT from React's, BSD-2-Clause from
 * Markdig's own, the PostgreSQL License from Npgsql's own, Apache-2.0 from
 * the canonical copy in detect-libc, MS-PL from AvalonDock's. {copyright}
 * is the package's own notice, from its metadata. Two are one project's own
 * wording, so they apply only to that project; any other package under
 * those licenses fails until its own text is added.
 */
const TEMPLATES = join(ROOT, 'scripts/deps/licenses')
const TEMPLATE_ONLY_FOR = {
  'BSD-2-Clause': /^Markdig$/,
  PostgreSQL: /^Npgsql(\.|$)/,
}

/**
 * Copyright notices for packages whose metadata names no holder, from the
 * license file in their own repository (checked by hand, with the date).
 */
const COPYRIGHT_OVERRIDES = {
  // github.com/ueberdosis/hocuspocus/blob/main/LICENSE.md, 2026-09-25
  '@hocuspocus/common': 'Copyright (c) 2023, Tiptap GmbH',
  '@hocuspocus/extension-database': 'Copyright (c) 2023, Tiptap GmbH',
  '@hocuspocus/server': 'Copyright (c) 2023, Tiptap GmbH',
}

const deps = []
const licenseFiles = new Map() // key -> text
const holders = new Map() // key -> copyright notice from the package's metadata
const missingFolders = new Set() // npm workspaces whose node_modules is not installed

/** A notice line from a metadata value that may or may not say "Copyright". */
function notice(value) {
  const v = value.trim()
  return /copyright|©|\(c\)/i.test(v) ? v : `Copyright (c) ${v}`
}

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
    const folder = join(ROOT, dir, path)
    if (!existsSync(folder)) missingFolders.add(dir)
    if (!licenseFiles.has(key)) {
      const found = readLicenseIn(folder)
      if (found) licenseFiles.set(key, found)
    }
    if (!holders.has(key) && existsSync(join(folder, 'package.json'))) {
      const meta = JSON.parse(readFileSync(join(folder, 'package.json'), 'utf8'))
      const author = typeof meta.author === 'string' ? meta.author : meta.author?.name
      // "Name <email> (url)": the name is the holder.
      const name = author?.replace(/\s*[<(].*$/, '').trim()
      if (name) holders.set(key, notice(name))
    }
  }
}

/**
 * A package's own license file, and any notice files beside it: an
 * Apache-2.0 package's NOTICE must travel with it, and a third-party notices
 * file credits code bundled inside the package (LIC-01). Notices are kept
 * apart from the license, since a package with only a notices file still
 * needs its license's text.
 */
function readLicenseIn(folder) {
  if (!existsSync(folder)) return null
  const files = readdirSync(folder).filter((f) => statSync(join(folder, f)).isFile())
  const read = (f) => readFileSync(join(folder, f), 'utf8').trim()
  const license = files.find((f) => /^(licen[cs]e|copying)(\.(md|txt|markdown))?$/i.test(f) || /^licen[cs]e[-_.]/i.test(f))
  const notices = files.filter((f) => /^(notice|third[-_]?party[-_]?notices)(\.(md|txt|markdown))?$/i.test(f)).sort()
  return { license: license ? read(license) : null, notices: notices.map((f) => ({ file: f, text: read(f) })) }
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
      const copyright = xml.match(/<copyright>([^<]+)<\/copyright>/i)?.[1] ?? xml.match(/<authors>([^<]+)<\/authors>/i)?.[1]
      if (copyright) holders.set(key, notice(copyright.replace(/&amp;/g, '&')))
      const found = readLicenseIn(folder) ?? { license: null, notices: [] }
      if (file && existsSync(join(folder, file[1].trim())))
        found.license = readFileSync(join(folder, file[1].trim()), 'utf8').trim()
      licenseFiles.set(key, found)
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
  // Tesria's own images (${TESRIA_IMAGES...}-app and so on, 14.2; the
  // migrate service reuses the app's) are built from this repository; their
  // contents are the packages below.
  for (const m of compose.matchAll(/^\s+image:\s*(\S+)/gm)) {
    if (!m[1].startsWith('tesria-') && !m[1].includes('TESRIA_IMAGES')) found.set(m[1], 'docker-compose.yml')
  }
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
  // The notices need node_modules to regenerate, which CI's check does not
  // install, so it checks the committed file instead: every package listed,
  // each with its license text (LIC-01).
  const notices = existsSync(OUT_NOTICES) ? readFileSync(OUT_NOTICES, 'utf8') : ''
  const sections = new Map(notices.split(`\n${'-'.repeat(78)}\n`).slice(1).map((sec) => {
    const lines = sec.split('\n')
    return [lines[0], lines.slice(4).join('\n').trim()]
  }))
  const incomplete = []
  for (const d of list) {
    if (d.ecosystem === 'Container') continue
    const body = sections.get(`${d.name} ${d.version} (${d.ecosystem})`)
    if (!body) incomplete.push(`${d.name} ${d.version} (${d.ecosystem})`)
  }
  if (incomplete.length) {
    console.error(`THIRD-PARTY-NOTICES.txt lacks a license text for ${incomplete.length} package(s):`)
    for (const i of incomplete.slice(0, 20)) console.error(`  ${i}`)
    console.error('Run node scripts/deps/build-manifest.mjs (after npm ci in src/web, collab and pdf) and commit it.')
    process.exit(1)
  }
  console.log(`dependency manifest current (${list.length} entries); notices complete`)
  process.exit(0)
}

/**
 * The text for a package that ships no license file: its own copyright
 * notice and its license's standard text, or null with the reason.
 */
function standardText(d, key) {
  const holder = COPYRIGHT_OVERRIDES[d.name] ?? holders.get(key)
  if (!holder) return { reason: 'its metadata names no copyright holder (add it to COPYRIGHT_OVERRIDES)' }
  const only = TEMPLATE_ONLY_FOR[d.license]
  const file = join(TEMPLATES, `${d.license}.txt`)
  if (!existsSync(file) || (only && !only.test(d.name)))
    return { reason: `there is no standard text for ${d.license} (add the package's own license text)` }
  const intro = `This package ships no license file; this is the standard ${d.license} license, with the copyright notice from the package's metadata.`
  if (d.license === 'Apache-2.0') {
    usesApache = true
    return { text: `${intro}\n\n${holder}\n\nLicensed under the Apache License, Version 2.0, whose full text is at the end of this file.` }
  }
  return { text: `${intro}\n\n${readFileSync(file, 'utf8').replace('{copyright}', holder).trim()}` }
}
let usesApache = false

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
let standard = 0
const noticeIndex = new Map() // text -> number
const failures = []
for (const d of list) {
  if (d.ecosystem === 'Container') continue
  const key = `${d.ecosystem === 'npm' ? 'npm' : 'nuget'}:${d.name}@${d.version}`
  if (seen.has(key)) continue
  seen.add(key)
  lines.push('-'.repeat(78), `${d.name} ${d.version} (${d.ecosystem})`, `License: ${d.license}`, d.url ?? '', '')
  const found = licenseFiles.get(key) ?? { license: null, notices: [] }
  if (found.license) { lines.push(found.license, ''); withText++ }
  else {
    const made = standardText(d, key)
    if (!made.text) { failures.push(`${d.name} ${d.version} (${d.ecosystem}): ${made.reason}`); continue }
    lines.push(made.text, ''); standard++
  }
  // Each distinct notice file once, at the end: many Microsoft packages
  // carry the same one.
  for (const n of found.notices) {
    if (!noticeIndex.has(n.text)) noticeIndex.set(n.text, noticeIndex.size + 1)
    lines.push(`Its ${n.file}: notice ${noticeIndex.get(n.text)} at the end of this file.`, '')
  }
}
for (const [text, number] of noticeIndex) {
  lines.push('='.repeat(78), `Notice ${number}`, '', text, '')
}
if (usesApache) {
  lines.push('='.repeat(78), 'Apache License, Version 2.0', '(referred to above)', '', readFileSync(join(TEMPLATES, 'Apache-2.0.txt'), 'utf8').trim(), '')
}

// Nothing is written unless every package has its notice (LIC-01): a
// silently bare entry is how 54 of them shipped without one.
if (missingFolders.size || failures.length) {
  if (missingFolders.size)
    console.error(`node_modules is missing in ${[...missingFolders].join(', ')}: run npm ci there first, so each package's own license file is read.`)
  for (const f of failures) console.error(`no license text for ${f}`)
  process.exit(1)
}
writeFileSync(OUT_JSON, manifest)
writeFileSync(OUT_NOTICES, lines.join('\n').replace(/\r\n/g, '\n').trimEnd() + '\n')
console.log(`wrote ${list.length} dependencies; license texts for ${seen.size} packages (${withText} their own, ${standard} standard)`)
