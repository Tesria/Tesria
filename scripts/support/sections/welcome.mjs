// Welcome to Tesria: the first page of the Support space.

export const shots = ({ demo }) => [
  { name: 'welcome-page', url: demo('Launch plan'), settle: 2500, steps: [{ wait: 2500 }] },
]

export async function build({ top, page, figure, doc, p, h, text, bold, ul, li, link, panel, live }) {
  const id = top['Welcome to Tesria']
  await page('Welcome to Tesria', null, doc(
    p('Tesria is a wiki your team runs on its own server. Write pages together, keep them organized in spaces, find anything with search, and export or publish what you write.'),
    ...(await figure(id, 'welcome-page', 'A page in Tesria',
      'A page in Tesria, with its space’s pages on the left. On a phone the same page fills the screen.')),
    h(2, 'Where to start'),
    ul(
      li(p(text('Setting up Tesria?', bold), ' Start with ', text('Getting started', bold), ': what you need, and a quick start that has it running in about ten minutes.')),
      li(p(text('Using Tesria?', bold), ' The ', text('User manual', bold), ' covers spaces, pages, the editor and every element in it, working together, and exporting.')),
      li(p(text('Running Tesria?', bold), ' ', text('Administration', bold), ' explains every admin tab and space setting, and ', text('Installation and operations', bold), ' covers configuration, HTTPS, backups and upgrades.')),
      li(p(text('Building on Tesria?', bold), ' See the ', text('REST API', bold), ' and ', text('MCP', bold), ' sections to connect scripts and AI assistants.')),
    ),
    panel('info', p('This site is written in Tesria and published with Tesria’s own website export, so everything on it is something Tesria can do.')),
    h(2, 'Everything on this site'),
    live('page-tree', { root: 'space', depth: '1' }),
  ))
}
