// A small Tesria client: list spaces, search, write a page, label it.
// Run with Node 18 or later:  node tesria.mjs
const base = process.env.TESRIA_URL      // such as https://wiki.example.com
const token = process.env.TESRIA_TOKEN   // starts with cct_
const space = process.env.TESRIA_SPACE   // a space key, such as TEAM

async function tesria(method, path, body) {
  const res = await fetch(base + path, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = res.status === 204 ? null : await res.json().catch(() => null)
  if (!res.ok) {
    const error = new Error(`${method} ${path}: ${res.status} ${data?.code ?? data?.title ?? ''}`)
    error.status = res.status
    error.data = data
    throw error
  }
  return data
}

// A page's content is a document: paragraphs, headings, lists and so on.
const paragraph = (text) => ({ type: 'paragraph', content: [{ type: 'text', text }] })
const documentOf = (...blocks) => JSON.stringify({ type: 'doc', content: blocks })

// 1. Which spaces can this token see?
for (const s of await tesria('GET', '/api/spaces')) console.log(s.key, s.name)

// 2. Search, in one space.
const { id: spaceId } = await tesria('GET', `/api/spaces/${space}`)
const hits = await tesria('GET', `/api/search?q=${encodeURIComponent('release')}&spaceId=${spaceId}`)
for (const hit of hits) console.log(hit.title, hit.pageId)

// 3. Write a page.
const page = await tesria('POST', '/api/pages', {
  spaceId,
  title: 'Release 1.4 notes',
  contentJson: documentOf(paragraph('Written by a script.')),
})
console.log('created', page.id, 'version', page.currentVersionNumber)

// 4. Change it without overwriting anyone: send the version you read.
async function append(pageId, text) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const current = await tesria('GET', `/api/pages/${pageId}`)
    const doc = JSON.parse(current.contentJson)
    doc.content.push(paragraph(text))
    try {
      return await tesria('PUT', `/api/pages/${pageId}`, {
        title: current.title,
        contentJson: JSON.stringify(doc),
        changeComment: 'Added a line',
        baseVersion: current.currentVersionNumber,
      })
    } catch (error) {
      if (error.status !== 409) throw error // someone else saved first: read it again
    }
  }
  throw new Error('The page kept changing; try again later.')
}
const updated = await append(page.id, 'And a second line.')
console.log('updated to version', updated.currentVersionNumber)

// 5. Label it.
await tesria('POST', `/api/pages/${page.id}/labels`, { name: 'release-notes' })
console.log('labeled')
