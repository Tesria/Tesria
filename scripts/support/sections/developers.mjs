// Developers (dev-plan 17): the part of the Support site for people who build
// on Tesria or work on it, below the user sections. REST API and MCP (api.mjs)
// live under it; this section writes the landing page and the pages of its
// own.

export const shots = []

export async function build({ page, doc, p, text, bold, live, pageLink }) {
  const b = (t) => text(t, bold)

  await page('Developers', null, doc(
    p('This part of the site is for people who build on Tesria or work on it. The rest of the site is about using Tesria; nothing here is needed for that.'),
    p(b('Connecting something to Tesria?'), ' ', pageLink('REST API'), ' is for scripts and other programs, and ', pageLink('MCP'), ' is for AI assistants. Both use an API token, which acts as the person who made it.'),
    live('children', { depth: '1', sort: 'position' }),
  ))
}
