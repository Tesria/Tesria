// Exports the Support space (dev-plan 10.5 step 7). Run it through
// export-support.sh.
//
//   support/support-pack.zip   the wiki pack, committed: the copy of the
//                              site that survives a reset of the instance
//   <out>/support-site.zip     the static site, for tesria.com; not
//                              committed, because it is rebuilt from the pack
//
// The site is also checked against Cloudflare's limits for static assets on
// the free plan: 25 MiB per file and 20,000 files (checked 2026-09-22).

import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import * as lib from '../lib/tesria.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const ROOT = join(HERE, '..', '..')
const OUT = process.argv[2] || join(ROOT, 'support')
const MAX_FILE = 25 * 1024 * 1024
const MAX_FILES = 20_000

const author = await lib.signIn(lib.need('SHOT_EMAIL'), lib.need('SHOT_PASSWORD'))
console.log(`signed in as ${author.me.email}`)

async function download(path) {
  const res = await author.call('GET', path, undefined, true)
  if (!res.ok) throw new Error(`GET ${path} -> ${res.status} ${(await res.text()).slice(0, 300)}`)
  return Buffer.from(await res.arrayBuffer())
}

/** The entries of a zip, from its central directory: name and uncompressed size. */
function entries(zip) {
  let end = zip.length - 22
  while (end >= 0 && zip.readUInt32LE(end) !== 0x06054b50) end--
  if (end < 0) throw new Error('not a zip file')
  const count = zip.readUInt16LE(end + 10)
  let at = zip.readUInt32LE(end + 16)
  const list = []
  for (let i = 0; i < count; i++) {
    const nameLength = zip.readUInt16LE(at + 28), extra = zip.readUInt16LE(at + 30), comment = zip.readUInt16LE(at + 32)
    list.push({ name: zip.toString('utf8', at + 46, at + 46 + nameLength), size: zip.readUInt32LE(at + 24) })
    at += 46 + nameLength + extra + comment
  }
  return list
}

const pack = await download('/api/spaces/DOCS/export/pack')
mkdirSync(join(ROOT, 'support'), { recursive: true })
writeFileSync(join(ROOT, 'support', 'support-pack.zip'), pack)
console.log(`pack: support/support-pack.zip, ${(pack.length / 1048576).toFixed(1)} MiB, ${entries(pack).length} files`)

// The site as its reader will see it would be audience=anonymous, but that
// needs anonymous reading on for the instance. As the author is the same
// set of pages here: nothing in Support is restricted.
const site = await download('/api/spaces/DOCS/export/site?audience=me')
mkdirSync(OUT, { recursive: true })
writeFileSync(join(OUT, 'support-site.zip'), site)
const files = entries(site).filter((e) => !e.name.endsWith('/'))
const biggest = files.reduce((a, b) => (b.size > a.size ? b : a), { size: 0 })
const total = files.reduce((n, e) => n + e.size, 0)
console.log(`site: ${join(OUT, 'support-site.zip')}, ${files.length} files, ${(total / 1048576).toFixed(1)} MiB unpacked`)
console.log(`largest file: ${biggest.name}, ${(biggest.size / 1048576).toFixed(2)} MiB`)

const problems = []
if (files.length > MAX_FILES) problems.push(`${files.length} files, over Cloudflare's ${MAX_FILES}`)
for (const f of files) if (f.size > MAX_FILE) problems.push(`${f.name} is ${(f.size / 1048576).toFixed(1)} MiB, over Cloudflare's 25 MiB`)
if (problems.length) {
  for (const p of problems) console.error(`  ${p}`)
  process.exit(1)
}
console.log('within Cloudflare\'s limits')
