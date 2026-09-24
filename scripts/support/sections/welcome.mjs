// Welcome to Tesria: the first page of the Support space, and the front door
// of tesria.com for someone who has never heard of Tesria (rewritten to the
// owner's rules of 2026-09-23, scripts/support/WRITING.md). It says what
// Tesria is, who it suits, and where to go next; the Features page, second in
// the tree, is the full tour, so this page does not repeat it.
//
// Run it on its own: scripts/support/publish-support.sh welcome

// One picture: what Tesria looks like, which words cannot show. A whole
// window, so on a phone its text is small; it is there for the shape of the
// thing (the tree on the left, the page on the right), not to be read.
export const shots = ({ demo }) => [
  { name: 'welcome-page', url: demo('Launch plan'), phone: false, settle: 2500, steps: [{ wait: 2500 }] },
]

export async function build({ top, page, doc, p, h, text, bold, italic, ul, li, panel, live, picture, pageLink }) {
  const b = (t) => text(t, bold)
  const i = (t) => text(t, italic)
  const welcome = top['Welcome to Tesria']

  await page('Welcome to Tesria', null, doc(
    p('Tesria is a wiki: a shared place where a group of people write things down, keep them in order, and find them again. How the office Wi-Fi is set up, what was decided at Tuesday’s meeting, the steps for onboarding a new hire, the recipe everyone asks for. Instead of living in someone’s inbox or in a document only one person can find, it lives on a page that everyone can read and anyone allowed can improve.'),
    p('What makes Tesria different is where it runs: on a computer ', i('you'), ' choose, such as a server at work, a small cloud machine, or a spare computer at home. Your pages, pictures and backups are kept there. Tesria is free and open source, and this site will help you from the first install to the hundredth page.'),
    ...(await picture(welcome, 'welcome-page', 'A page in Tesria, with its space’s pages listed on the left',
      'A page in Tesria. The space’s pages are listed on the left; the page you are reading fills the rest.')),

    h(2, 'Who Tesria is for'),
    ul(
      li(p(b('Teams that are tired of hunting for things.'), ' If the answer to “where is that written down?” is usually “ask Sam”, a wiki gives it a home, and everyone on the team can find it without asking.')),
      li(p(b('Organizations that need to keep their information in-house.'), ' Because Tesria runs on your own machine, your pages and files are stored there, not on another company’s servers, and you decide who can reach it.')),
      li(p(b('Clubs, schools and households with a spare computer.'), ' Tesria runs happily on a home network, and everyone can reach it from their phone, tablet or laptop.')),
      li(p(b('Anyone who wants to try it first.'), ' You can run Tesria on your own laptop, look around, and decide later where it should live.')),
    ),

    h(2, 'Where to go next'),
    ul(
      li(p(b('Curious what it can do?'), ' ', pageLink('Features'), ' is a tour of everything Tesria does, grouped by what you are trying to get done. ', pageLink('What is Tesria'), ' explains the idea of a wiki, and why a team would run its own.')),
      li(p(b('Ready to try it?'), ' The ', pageLink('Quick start'), ' takes you from a computer with Docker to your own Tesria, step by step. ', pageLink('Prerequisites'), ' says what to have ready first.')),
      li(p(b('Already have a Tesria to use?'), ' The ', pageLink('User manual'), ' covers writing pages, organizing them into spaces, and working with other people. ', pageLink('Your first space and page'), ' is the gentlest place to begin.')),
      li(p(b('Looking after one?'), ' ', pageLink('Installation and operations'), ' covers settings, secure connections, backups and upgrades, and ', pageLink('Administration'), ' explains every screen an administrator sees.')),
      li(p(b('Connecting other software?'), ' See the ', pageLink('REST API'), ' for scripts and other systems, and ', pageLink('MCP'), ' for AI assistants.')),
      li(p(b('Stuck?'), ' Try ', pageLink('Troubleshooting'), ' and the ', pageLink('FAQ'), '. If a word is new to you, the ', pageLink('Glossary'), ' explains it.')),
    ),

    panel('info', p(b('This site is made with Tesria.'), ' Every page here was written in Tesria and published with its own website export, so anything you see on it is something your Tesria can do too.')),

    h(2, 'Everything on this site'),
    live('page-tree', { root: 'space', depth: '1' }),
  ))
}

/** The phone picture the first version showed beside the desktop one. */
export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/DOCS')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const node = tree.find((n) => n.title === 'Welcome to Tesria')
  if (!node) return
  for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
    if (a.filename === 'welcome-page.phone.png') {
      await author.call('DELETE', `/api/attachments/${a.id}`)
      console.log(`  - Welcome to Tesria: ${a.filename}`)
    }
  }
}
