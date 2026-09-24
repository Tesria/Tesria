// User manual, Basics: signing in, accounts, getting around (dev-plan 10.5).
//
// Facts from the sign-in, registration, recovery, profile and layout
// components, gathered 2026-09-23. Labels are quoted as the app shows them.

const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

export const shots = ({ demo }) => [
  { name: 'sign-in', url: '/login', anon: true, settle: 1200, steps: [{ wait: 1500 }], clipTo: 'form.authcard', clipPad: 24 },
  { name: 'register-invited', url: '/register?invite=example', anon: true, settle: 1200, steps: [{ wait: 1500 }], clipTo: 'form.authcard', clipPad: 24 },
  { name: 'recover', url: '/recover', anon: true, settle: 1200, steps: [{ wait: 1500 }], clipTo: 'form.authcard', clipPad: 24 },
  { name: 'two-factor-section', url: '/profile', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Two-factor sign-in') },
  { name: 'recovery-codes-section', url: '/profile', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Recovery codes') },
  { name: 'top-bar', url: demo('Launch plan'), settle: 1500, steps: [{ wait: 2500 }], clipTo: 'header.topbar', phone: { clipTo: 'header.topbar' } },
  { name: 'page-layout', url: demo('Launch plan'), settle: 1500, steps: [{ wait: 2500 }] },
  {
    name: 'page-menu', url: demo('Launch plan'), settle: 800,
    steps: [{ wait: 2500 }, { click: '.page-actionbar .overflow-menu__trigger' }, { wait: 400 }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
  },
  {
    name: 'theme-menu', url: demo('Launch plan'), settle: 800,
    steps: [{ wait: 2500 }, { click: '.theme-toggle' }, { wait: 400 }],
    clipTo: '.theme-menu__panel', clipPad: 12,
  },
  { name: 'tour-and-tips', url: '/profile', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Tour and tips') },
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, live }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  await page('User manual', null, doc(
    p('How to use Tesria: reading, writing and working together. Every element of the editor has a page of its own, and ', b('Tesria on phones and tablets'), ' covers what changes on a small screen.'),
    live('children', { depth: '2', sort: 'position' }),
  ))

  const basics = await ensure('Basics', manual)
  await page('Basics', manual, doc(
    p('Signing in, keeping your account safe, and finding your way around.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ------------------------------------------------------------- Signing in
  const signIn = await ensure('Signing in', basics)
  await page('Signing in', basics, doc(
    p('Open your Tesria’s address and sign in with your email address and password.'),
    ...(await figure(signIn, 'sign-in', 'The sign-in page')),
    ul(
      li(p('The eye button in the password field shows what you typed.')),
      li(p('If your organization uses single sign-on, a button such as ', b('Sign in with Company SSO'), ' appears under the form. Use it instead of a password.')),
      li(p(b('Forgot your password?'), ' starts a reset: see ', b('Resetting a password'), '.')),
      li(p(b('No account? Create one'), ' appears when anyone may join, or when you arrived from an invite link.')),
      li(p(b('Browse what is public'), ' appears when some spaces can be read without signing in.')),
    ),
    p('After signing in you go back to the page you were trying to open, or to the list of spaces.'),
    h(2, 'Two-factor sign-in'),
    p('If your account has two-factor on, a second step asks for the six-digit code from your authenticator app. One of your recovery codes works in its place; each code works once. The second step has to be finished within five minutes, or you start again.'),
    h(2, 'Too many wrong attempts'),
    p('After 5 wrong passwords or codes the account is locked for a minute, and each further lock is twice as long, up to 15 minutes. While it is locked even the right password is refused. A successful sign-in or a password reset clears it, and an administrator can unlock it in ', b('Administration → Users'), '.'),
    p('Too many attempts from one address in a minute gets ', b('Too many attempts. Wait a minute and try again.')),
    h(2, 'How long you stay signed in'),
    p('A session ends after 14 days without use, and 90 days after you signed in whatever you do. ', b('Sign out'), ' in the top bar ends this session on the server, not just in the browser. Your profile lists every session and can sign out the others.'),
    panel('info', p('Some administrative actions ask for your password again if you signed in more than five minutes ago, in a box headed ', b('Confirm it’s you'), '. A code from your authenticator app works instead.')),
  ))

  // ---------------------------------------------------- Accounts and invites
  const accounts = await ensure('Accounts and invites', basics)
  await page('Accounts and invites', basics, doc(
    p('Whether you can create your own account depends on the instance. An administrator chooses between ', b('Open'), ', where anyone who can reach the address may join, and ', b('Invite only'), '.'),
    h(2, 'Joining with an invite'),
    p('An invite is a link an administrator sends you. Opening it shows the sign-up form with the note ', b('You were invited to this instance.')),
    ...(await figure(accounts, 'register-invited', 'Creating an account from an invite')),
    ul(
      li(p('Enter your ', b('Display name'), ', ', b('Email'), ' and a ', b('Password'), ' of at least 8 characters, then ', b('Create account'), '.')),
      li(p('An invite works once, and expires after 7 days unless the administrator chose otherwise (up to 90).')),
      li(p('An invite made for one email address works only for that address.')),
    ),
    p('Straight after creating the account, Tesria shows your ', b('recovery codes'), '. Save them before going on: see ', b('Two-factor and recovery codes'), '.'),
    h(2, 'Without an invite'),
    p('On an open instance, ', b('No account? Create one'), ' on the sign-in page leads to the same form. On an invite-only instance the form refuses with ', b('Registration is by invitation on this instance.')),
    h(2, 'Inviting other people'),
    p('Administrators create invites in ', b('Administration → Invites'), '. Anyone else whose role allows it has an ', b('Invite people'), ' link in the top bar. Either way:'),
    ol(
      li(p('Optionally enter the person’s email address, so the invite works for them alone.')),
      li(p('Choose how many days it stays valid, then ', b('Create invite'), '.')),
      li(p('Copy the link and send it yourself. It is shown once.')),
    ),
    p('The list shows each invite as ', b('Unused'), ', ', b('used'), ' (with who used it) or ', b('expired'), ', and ', b('Revoke'), ' cancels one that has not been used.'),
  ))

  // --------------------------------------------- Two-factor and recovery codes
  const twoFactor = await ensure('Two-factor and recovery codes', basics)
  await page('Two-factor and recovery codes', basics, doc(
    p('Two-factor sign-in adds a second step: after your password, a six-digit code from an app on your phone. Someone who learns your password still cannot get in without the phone.'),
    h(2, 'Turning on two-factor'),
    ol(
      li(p('Open your profile (your name in the top bar) and find ', b('Two-factor sign-in'), '.')),
      li(p('Enter your current password (not needed in the first few minutes after signing in) and choose ', b('Set up two-factor'), '.')),
      li(p('Scan the QR code with an authenticator app such as 1Password, Google Authenticator, Authy or Aegis. If you cannot scan, type the key shown under it.')),
      li(p('Enter the six-digit code the app shows and choose ', b('Turn on'), '.')),
    ),
    ...(await figure(twoFactor, 'two-factor-section', 'Two-factor sign-in on the profile page')),
    p('Turning it on signs out your other devices. Turning it off needs your password or a code, and does the same. On some instances administrators must keep two-factor on, and the button to turn it off is not offered.'),
    p('Lost the phone and every recovery code? An administrator can turn your two-factor off from Administration → Users, after which you sign in with your password and set it up again.'),
    panel('note', p('If you sign in through single sign-on, two-factor is your identity provider’s job, and this section says so.')),
    h(2, 'Recovery codes'),
    p('Recovery codes are your way back in if you lose your phone or forget your password. You get 8, each in the form ', c('XXXX-XXXX-XXXX'), ', and each works once:'),
    ul(
      li(p('at the two-factor step, in place of the code from your app;')),
      li(p('on the password reset page, in place of an email link.')),
    ),
    p('They are shown when you create your account and whenever you make new ones, with ', b('Download'), ' and ', b('Copy'), ' buttons. Keep them somewhere other than your phone, such as a password manager or on paper.'),
    ...(await figure(twoFactor, 'recovery-codes-section', 'Recovery codes on the profile page')),
    p('Your profile shows how many are left, and warns when 2 or fewer remain. ', b('Generate new codes'), ' makes a new set and cancels the old one at once.'),
    panel('warning', p('Treat recovery codes like a password: anyone who has one can get into your account.')),
  ))

  // -------------------------------------------------- Resetting a password
  const reset = await ensure('Resetting a password', basics)
  await page('Resetting a password', basics, doc(
    p('Choose ', b('Forgot your password?'), ' on the sign-in page. There are three ways back in, depending on how your Tesria is set up.'),
    ...(await figure(reset, 'recover', 'Resetting a password')),
    h(2, 'By email'),
    p('Where Tesria sends email, enter your address and choose ', b('Email me a reset link'), '. The link works once and expires in an hour. The page says the same thing whether or not the address has an account, so nobody can use it to find out who has one.'),
    h(2, 'With a recovery code'),
    p('Choose ', b('Use a recovery code instead'), ', then enter your email, one recovery code, and your new password twice. The code is used up.'),
    h(2, 'From an administrator'),
    p('If you have neither, an administrator can make you a one-time reset link in ', b('Administration → Users → Reset password'), '. It works once, for an hour; hand it over in person or by a channel you trust.'),
    h(2, 'Afterwards'),
    p('Choosing a new password signs out every other device on your account, and clears any lockout. You are not signed in automatically: sign in with the new password.'),
  ))

  // --------------------------------------------- Finding your way around
  const around = await ensure('Finding your way around', basics)
  await page('Finding your way around', basics, doc(
    ...(await figure(around, 'page-layout', 'A page in Tesria')),
    h(2, 'The top bar'),
    ...(await figure(around, 'top-bar', 'The top bar')),
    table([
      ['Item', 'What it does'],
      ['The logo', 'Back to the list of spaces.'],
      ['Spaces', 'Every space you can see.'],
      ['Admin', 'Administration, if your role has any administrative right.'],
      ['Search pages…', 'Searches every page you can read.'],
      ['The sun or moon', 'Theme and accent color; see Theme and accent.'],
      ['The bell', 'Your notifications.'],
      ['Your name', 'Your profile.'],
      ['Sign out', 'Ends this session.'],
    ], [200, 500]),
    p('On a phone, the ☰ button opens the same items as a menu.'),
    h(2, 'The space sidebar'),
    p('Inside a space, the sidebar on the left shows the space’s name, ', b('+ New page'), ', the tree of pages, and ', b('Space settings'), ' at the bottom. The button beside the space name hides the sidebar; your choice is remembered on this device.'),
    h(2, 'The page bar'),
    p('Above every page: ', b('Edit'), ' if you may edit it, ', b('Full width'), ' to widen the page for everyone, and ', b('⋮'), ' for everything else.'),
    ...(await figure(around, 'page-menu', 'The ⋮ menu on a page')),
    ul(
      li(p(b('Export as Markdown, HTML or PDF'), ', where the space allows it.')),
      li(p(b('Watch this page'), ', to be told when it changes.')),
      li(p(b('Save as template'), ', to start new pages from this one.')),
      li(p(b('Delete'), ', which moves the page and its sub-pages to the trash.')),
    ),
    p('Below the page are four tabs: ', b('Comments'), ', ', b('Attachments'), ', ', b('History'), ' and ', b('Restrictions'), '. The breadcrumb above the title shows where the page sits in its space.'),
  ))

  // ----------------------------------------------------- Tour and tips
  const tour = await ensure('The tour and tips', basics)
  await page('The tour and tips', basics, doc(
    p('The first time you sign in, a short tour shows five things: spaces and pages, writing, working together, finding things, and your profile. ', b('Next'), ' and ', b('Back'), ' move through it, and ', b('Skip the tour'), ' ends it. Leaving it any other way counts as skipping it.'),
    p('On its last screen, ', b('Show me tips as I go'), ' turns on tips: small cards that point out one thing at a time as you work.'),
    ul(
      li(p('At most three a day, each shown only once, and never over a menu or dialog.')),
      li(p(b('Got it'), ' dismisses that tip for good; ', b('Turn off tips'), ' stops them all.')),
    ),
    h(2, 'Changing your mind'),
    p('Your profile has a ', b('Tour and tips'), ' section: ', b('Show the tour again'), ', the ', b('Show tips as I go'), ' switch, and a button to bring back tips you dismissed.'),
    ...(await figure(tour, 'tour-and-tips', 'Tour and tips on the profile page')),
  ))

  // --------------------------------------------------- Theme and accent
  const theme = await ensure('Theme and accent', basics)
  await page('Theme and accent', basics, doc(
    p('The sun, moon or screen button in the top bar sets how Tesria looks on this device.'),
    ...(await figure(theme, 'theme-menu', 'The appearance menu')),
    ul(
      li(p(b('Theme'), ': ', b('System'), ' follows your computer or phone, or choose ', b('Light'), ' or ', b('Dark'), '.')),
      li(p(b('Accent color'), ': Blue, Teal, Green, Purple, Orange or Magenta, used for buttons, links and highlights. An instance with its own brand color lists it first.')),
    ),
    p('The choice is kept in this browser, so a phone and a laptop can differ.'),
    panel('note', p('An administrator can fix the theme or the accent for everyone in ', b('Administration → Branding'), '. A fixed choice disappears from this menu, and if both are fixed the button disappears.')),
  ))
}
