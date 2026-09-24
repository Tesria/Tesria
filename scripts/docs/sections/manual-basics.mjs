// User manual, Basics (dev-plan 10.5, rewritten to the rules of 15.6):
// signing in, accounts, keeping an account safe, and getting around.
//
// These are the first pages a new user reads, so each explains what a thing
// is and why it matters before the steps. Facts were checked against
// LoginPage, RegisterPage, RecoverPage, ProfilePage, TotpSection,
// RecoveryCodes(Section, Prompt), Layout, SpacePage, PageTree, treeFilter,
// treeMarkers, SidebarResizer, ThemeToggle, WelcomePage, TipHost and
// TourAndTipsSection, and the auth, invite and recovery endpoints, on
// 2026-09-24. Labels are quoted as the app shows them.
//
// Every picture is a close-up taken in a narrow window, except the whole
// page, and none has a phone version: the phone is covered by the User
// manual's own phone chapter. The sidebar pictures use a 900px window,
// because below 640px the sidebar moves into the phone menu.
//
// No page to add or remove.

const NARROW = { width: 480, height: 900 }
// Wide enough for the space sidebar, which is hidden below 640px.
const SIDEBAR = { width: 900, height: 640 }
const BLUR = { eval: 'document.activeElement && document.activeElement.blur()' }
// The recovery page opens on the emailed link where email is set up; the
// picture is of the recovery code form, which every instance has.
const CODE_FORM = "[...document.querySelectorAll('form.authcard button.link-btn')].find((b) => b.textContent.includes('Use a recovery code instead'))?.click()"

export const shots = ({ demo }) => [
  // ---- Signing in: the form, with the two controls people look for.
  {
    name: 'sign-in', url: '/login', anon: true, viewport: NARROW, phone: false, settle: 800,
    steps: [
      { wait: 1500 },
      { type: 'alex.rivera@example.com', selector: 'form.authcard input[type="email"]' },
      { type: 'an example password', selector: 'form.authcard .password-input input' },
      BLUR,
    ],
    clipTo: 'form.authcard', clipPad: 24,
    annotate: [
      { type: 'box', target: 'form.authcard .password-input__toggle', pad: 4 },
      { type: 'box', target: 'form.authcard a[href="/recover"]', pad: 4 },
    ],
  },

  // ---- Accounts and invites: the sign-up form an invite link opens.
  {
    name: 'register-invited', url: '/register?invite=example', anon: true, viewport: NARROW, phone: false, settle: 800,
    steps: [
      { wait: 1500 },
      { type: 'Priya Natarajan', selector: 'form.authcard label:nth-of-type(1) input' },
      { type: 'priya@example.com', selector: 'form.authcard input[type="email"]' },
      { type: 'an example password', selector: 'form.authcard .password-input input' },
      BLUR,
    ],
    clipTo: 'form.authcard', clipPad: 24,
    annotate: [
      { type: 'box', target: 'form.authcard > p.small', pad: 4 },
      { type: 'box', target: 'form.authcard button[type="submit"]', pad: 4 },
    ],
  },

  // ---- Two-factor: where it is on the profile, and the scan step. The
  // account the pictures are taken as has two-factor off (the harness could
  // not sign in otherwise), so the section offers Set up two-factor. The
  // QR code and key are a real, unused secret for that account: blurred.
  {
    name: 'two-factor-section', url: '/profile', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2500 }],
    clipTo: '#two-factor', clipPad: 8,
    annotate: [{ type: 'box', target: '#two-factor button[type="submit"]', pad: 4 }],
  },
  {
    name: 'two-factor-enroll', url: '/profile', viewport: NARROW, phone: false, settle: 800,
    steps: [
      { wait: 2500 },
      { click: '#two-factor button[type="submit"]' },
      { waitFor: '.totp-enroll img' }, { wait: 600 },
      { css: '.totp-enroll img { filter: blur(10px); } .totp-secret { filter: blur(5px); }' },
    ],
    clipTo: '#two-factor', clipPad: 8,
    annotate: [
      { type: 'box', target: '.totp-enroll img', pad: 4 },
      { type: 'box', target: '.totp-enroll label', pad: 4 },
      { type: 'box', target: '.totp-enroll .btn--primary', pad: 4 },
    ],
  },
  {
    name: 'recovery-codes-section', url: '/profile', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2500 }],
    clipTo: '#two-factor + section', clipPad: 8,
    annotate: [{ type: 'box', target: '#two-factor + section > button', pad: 4 }],
  },

  // ---- Resetting a password: the recovery code form.
  {
    name: 'recover', url: '/recover', anon: true, viewport: NARROW, phone: false, settle: 800,
    steps: [
      { wait: 1500 }, { eval: CODE_FORM }, { wait: 400 },
      { type: 'priya@example.com', selector: 'form.authcard input[type="email"]' },
      BLUR,
    ],
    clipTo: 'form.authcard', clipPad: 24,
    annotate: [{ type: 'box', target: 'form.authcard label:nth-of-type(2)', pad: 4 }],
  },

  // ---- Finding your way around: the whole window once, then the parts.
  { name: 'page-layout', url: demo('Launch plan'), viewport: { width: 1024, height: 640 }, phone: false, settle: 1500, steps: [{ wait: 2500 }] },
  {
    name: 'sidebar', url: demo('Launch plan'), viewport: SIDEBAR, phone: false, settle: 800,
    // Hovering the handle shows the line that marks it.
    steps: [{ wait: 2500 }, { hover: '.sidebar__resize' }, { wait: 300 }],
    clipTo: '.sidebar', clipPad: 8,
    annotate: [
      { type: 'box', target: '.sidebar__toggle--hide', pad: 3 },
      { type: 'arrow', target: '.sidebar__resize', from: 'left', len: 50, gap: 4, label: 'Drag to resize' },
    ],
  },
  {
    name: 'page-menu', url: demo('Launch plan'), viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '.page-actionbar .overflow-menu__trigger', pad: 4 }],
  },
  {
    name: 'tree-style', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2500 }],
    clipTo: '#page-tree', clipPad: 8,
    annotate: [{ type: 'box', target: 'label.tree-style__option:nth-of-type(2)', pad: 3 }],
  },
  // Filtered by "meeting": Meeting notes matches, its parent is shown dimmed,
  // and the meetings under it show because the toggle is on. Cropped from the
  // Pages heading to the last row shown, as the tree fills the sidebar's height.
  {
    name: 'tree-filter', url: demo('Launch plan'), viewport: SIDEBAR, phone: false, settle: 800,
    steps: [{ wait: 2500 }, { type: 'meeting', selector: '.sidebar .tree-filter__input' }, BLUR, { wait: 400 }],
    clipTo: ['.sidebar .tree-section__heading', '.sidebar .tree > a:last-of-type'], clipPad: 10,
    annotate: [{ type: 'box', target: '.sidebar .tree-filter__children', pad: 3 }],
  },
  // The filter is kept for the browser tab, so it would carry into any later
  // picture of Tesria Demo's sidebar.
  { name: 'tree-filter-cleared', skipCapture: true, phone: false, settle: 100, steps: [{ eval: "sessionStorage.removeItem('tesria-tree-filter:DEMO')" }] },

  // ---- The tour and tips.
  {
    name: 'tour', url: '/welcome', viewport: NARROW, phone: false, settle: 1500,
    steps: [{ wait: 2500 }],
    clipTo: '.tour__card', clipPad: 12,
    annotate: [{ type: 'box', target: '.tour__skip', pad: 4 }],
  },
  {
    name: 'tour-and-tips', url: '/profile', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2500 }],
    clipTo: '#tour-and-tips', clipPad: 8,
    annotate: [{ type: 'box', target: '#tour-and-tips .row-gap button:first-child', pad: 4 }],
  },

  // ---- Theme and accent: the button and its menu, as on a computer.
  {
    name: 'theme-menu', url: demo('Launch plan'), viewport: SIDEBAR, phone: false, settle: 800,
    steps: [{ wait: 2500 }, { click: '.theme-toggle' }, { wait: 400 }],
    clipTo: ['.topbar__right', '.theme-menu__panel'], clipPad: 10,
    annotate: [{ type: 'box', target: '.theme-toggle', pad: 4 }],
  },
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, live, picture, pageLink, adminAt }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  const manual = top['User manual']

  // Every page first, so the Basics page can link to all of them.
  const basics = await ensure('Basics', manual)
  const signIn = await ensure('Signing in', basics)
  const accounts = await ensure('Accounts and invites', basics)
  const twoFactor = await ensure('Two-factor and recovery codes', basics)
  const reset = await ensure('Resetting a password', basics)
  const around = await ensure('Finding your way around', basics)
  const tour = await ensure('The tour and tips', basics)
  const theme = await ensure('Theme and accent', basics)

  // ============================================================ User manual
  await page('User manual', null, doc(
    p('This is the guide to using Tesria from day to day: finding your way around, writing and organizing pages, and working with other people. Each part starts with what a thing is and why you would want it, then shows you how.'),
    p('New to Tesria? Start with ', pageLink('Basics'), '. It covers signing in, keeping your account safe and getting around. Every element of the editor has a page of its own, and ', pageLink('Tesria on phones and tablets'), ' covers what changes on a small screen.'),
    live('children', { depth: '2', sort: 'position' }),
  ))

  // ================================================================= Basics
  await page('Basics', manual, doc(
    p('These pages are for your first day with Tesria. They cover getting in, keeping your account safe, and finding your way around, in the order you will need them. If someone has just sent you an invite link, start at the top.'),
    ol(
      li(p(pageLink('Accounts and invites'), ': how you get an account, and how to invite other people.')),
      li(p(pageLink('Signing in'), ': the sign-in page, what each link on it is for, and what to do when it will not let you in.')),
      li(p(pageLink('Two-factor and recovery codes'), ': the two things that keep your account yours, even if someone learns your password or you lose your phone.')),
      li(p(pageLink('Resetting a password'), ': the ways back in when you have forgotten it.')),
      li(p(pageLink('Finding your way around'), ': the top bar, the sidebar, the page tree and its filter, and search.')),
      li(p(pageLink('The tour and tips'), ': the short tour you see on your first visit, and the tips that follow.')),
      li(p(pageLink('Theme and accent'), ': light or dark, and the color of buttons and links.')),
    ),
    panel('success', p(b('Set up your recovery codes and two-factor on day one.'), ' It takes a few minutes, and it is much easier to do before you need them than after.')),
  ))

  // ============================================================= Signing in
  await page('Signing in', basics, doc(
    p('Tesria keeps each person’s work under their own account: your name goes on the pages you write and the comments you leave, the spaces you can see depend on who you are, and your notifications are yours. Signing in is how Tesria knows it is you.'),
    p('This page walks through the sign-in page, what each link on it is for, and what to do if it will not let you in. If you do not have an account yet, see ', pageLink('Accounts and invites'), ' first.'),
    panel('note', p(b('Throughout this page, '), c('your-server'), b(' stands for your Tesria’s address:'), ' whatever you type into the browser to open it, such as ', c('wiki.example.com'), '. Whoever runs your Tesria can tell you what it is.')),

    h(2, 'Signing in, step by step'),
    step(1, 'Open Tesria'),
    p('Type ', c('https://your-server'), ' into your browser’s address bar. If you are not signed in, Tesria shows the sign-in page. If some spaces can be read without an account, it shows those instead, with a ', b('Sign in'), ' button at the top right: choose it. Bookmark the address: you will come back to it.'),
    step(2, 'Enter your email address and password'),
    p('Use the email address your account was made with. The eye button at the end of the password box shows what you have typed, which helps on a phone keyboard; press it again to hide it.'),
    ...(await picture(signIn, 'sign-in', 'The sign-in page, with the show password button and Forgot your password? boxed', 'The eye shows your password as you type it. Forgot your password? is below Sign in.')),
    step(3, 'Choose Sign in'),
    p('Tesria takes you back to the page you were trying to open, or to the list of spaces if you came straight to the sign-in page.'),

    h(2, 'If your account has two-factor sign-in'),
    p('After your password, a second screen headed ', b('One more step'), ' asks for the six-digit code from the authenticator app on your phone. Open the app, type the code Tesria shows for this account, and choose ', b('Sign in'), '.'),
    ul(
      li(p(b('The code changes every 30 seconds.'), ' If Tesria says the code is not right, wait for the next one and type that.')),
      li(p(b('No phone to hand?'), ' Type one of your recovery codes instead. Each one works once.')),
      li(p(b('Finish within five minutes.'), ' After that the second step expires and you start again from your password. ', b('Start over'), ' does the same at any time.')),
    ),
    p('To turn two-factor on, see ', pageLink('Two-factor and recovery codes'), '.'),

    h(2, 'Other links on the sign-in page'),
    p('Which of these you see depends on how your Tesria is set up:'),
    ul(
      li(p(b('Sign in with …'), ', under the form, appears when your organization uses single sign-on (for example ', i('Sign in with Company SSO'), '). If you have it, use it instead of a password: your organization’s own sign-in page opens, and it brings you back here.')),
      li(p(b('Forgot your password?'), ' starts a reset. See ', pageLink('Resetting a password'), '.')),
      li(p(b('No account? Create one'), ' appears when anyone may join, or when you arrived from an invite link.')),
      li(p(b('Browse what is public'), ' appears when some spaces can be read without signing in.')),
      li(p(b('Trust this device'), ' appears on a server that makes its own security certificate, under ', i('Did your browser warn that this site is not secure?'), '. It opens a short guide that stops the warning for good. See ', pageLink('Trusting the local certificate'), '.')),
    ),

    h(2, 'When it will not let you in'),
    p(b('Incorrect email or password.'), ' means one of the two is wrong. Tesria does not say which, on purpose: otherwise anyone could type addresses in to find out who has an account. Check the address first, then the password (the eye button helps).'),
    p('To stop someone guessing their way in, Tesria locks an account after too many wrong tries. Unless your administrator has changed the numbers:'),
    ul(
      li(p('After ', b('5 wrong passwords or codes'), ' in a row, the account is locked for a minute.')),
      li(p('Each wrong try after that locks it again, for twice as long each time, up to 15 minutes.')),
      li(p('While it is locked, even the right password is refused, with the same message. Wait, then try again.')),
    ),
    p('The lock ends by itself. Signing in successfully or resetting your password clears the count, and an administrator can unlock the account straight away in ', ...adminAt('Users'), '.'),
    p('Separately, too many sign-in attempts from one place in a short time gets ', b('Too many attempts. Wait a minute and try again.'), ' Waiting a minute is all it takes.'),

    h(2, 'How long you stay signed in'),
    p('You stay signed in on a device until you sign out, with two limits: a session ends after ', b('14 days'), ' without being used, and ', b('90 days'), ' after you signed in, however much you use it. Then you sign in again.'),
    p(b('Sign out'), ', at the right of the top bar, ends this session on the server as well as in the browser, so a copy of it cannot be used later. Always sign out on a computer other people use. To see every device signed in to your account, and sign out the ones you do not recognize, see ', pageLink('Sessions'), '.'),
    panel('info', p(b('Asked for your password again?'), ' Some administrative actions, such as ones that cannot be undone, ask you to confirm your password in a box headed ', b('Confirm it’s you'), ' if you signed in more than a few minutes ago. A code from your authenticator app works instead. It makes sure the person at the keyboard is still you.')),
  ))

  // =================================================== Accounts and invites
  await page('Accounts and invites', basics, doc(
    p('Everyone who uses Tesria has their own account: an email address, a password and a display name. Having your own, rather than sharing one, is what lets Tesria put your name on what you write, show you only the spaces meant for you, and tell you when something you care about changes.'),
    p('There are two ways to get an account, and your Tesria’s administrator decides which apply:'),
    ul(
      li(p(b('An invite link.'), ' Someone sends you a link that lets you create an account. This always works, whatever else is set.')),
      li(p(b('Signing up yourself.'), ' If the administrator has turned on ', b('Allow public registration'), ', anyone who can reach your Tesria can create an account from the sign-in page.')),
    ),

    h(2, 'Joining with an invite'),
    step(1, 'Open the link'),
    p('An invite is a link that starts with your Tesria’s address and ends in ', c('/register?invite='), ' and a long code. Open it and the sign-up form appears, with the note ', b('You were invited to this instance.')),
    step(2, 'Fill in the form'),
    ...(await picture(accounts, 'register-invited', 'The Create account form opened from an invite', 'The note at the top says you came from an invite. Create account is at the end.')),
    ul(
      li(p(b('Display name:'), ' how you appear to everyone else, on pages, comments and history. Your full name is usual. You can change it later.')),
      li(p(b('Email:'), ' the address you will sign in with. If the invite was made for one address, it has to be that one.')),
      li(p(b('Password:'), ' at least 8 characters. A few unrelated words make a password that is long, hard to guess and easy to remember.')),
    ),
    step(3, 'Choose Create account'),
    p('You are signed in straight away.'),
    step(4, 'Save your recovery codes'),
    p('Before anything else, Tesria shows you eight ', b('recovery codes'), '. They are your way back in if you forget your password or lose your phone, and this is the only time they are shown, so save them now: ', b('Download'), ' saves them as a text file, and ', b('Copy'), ' copies them to paste into a password manager. Then tick ', b('I have saved these codes somewhere safe'), ' and choose ', b('Continue to Tesria'), '. ', pageLink('Two-factor and recovery codes'), ' explains what they are for and where to keep them.'),
    panel('note', p(b('An invite works once, and not forever.'), ' It expires after the number of days its sender chose (7 unless they changed it). If yours no longer works, ask for a new one.')),

    h(2, 'Signing up without an invite'),
    p('If your Tesria is open to everyone, the sign-in page shows ', b('No account? Create one'), '. It leads to the same form and the same recovery codes. If it is not open, the link is not there, and trying anyway gets ', b('Registration is by invitation on this instance.'), ' Ask your administrator for an invite.'),
    p('Each email address can have only one account. ', b('An account with this email already exists.'), ' means you, or someone, already signed up with it: sign in instead, or reset the password.'),

    h(2, 'Inviting other people'),
    p('Administrators make invites in ', ...adminAt('Invites'), '. Other people whose role allows it have an ', b('Invite people'), ' link in the top bar, with the same form. It is how you add someone when your Tesria is not open to everyone.'),
    step(1, 'Say who it is for, if you like'),
    p('Type their address in ', b('Email (optional)'), ' and the invite works for that address only, so it is no use to anyone it is forwarded to. Leave it empty and whoever has the link can use it.'),
    step(2, 'Choose how long it lasts'),
    p(b('Expires in (days)'), ' is 7 to start with, and can be anything from 1 to 90. A short time is safer; a longer one suits someone who will not join for a while.'),
    step(3, 'Create it and send it yourself'),
    p('Choose ', b('Create invite'), '. The link appears once, with a ', b('Copy'), ' button: copy it and send it to the person yourself, by email or chat. Tesria does not send it for you, and cannot show it again.'),
    p('Each invite is listed as ', b('Unused'), ', ', b('used'), ' (with who used it and when) or ', b('expired'), '. ', b('Revoke'), ' stops an unused one from working, for example if it went to the wrong person. Someone who may create invites but not manage them does not see the list, so they should copy each link when it is shown. More in ', pageLink('Invites'), '.'),
  ))

  // =========================================== Two-factor and recovery codes
  await page('Two-factor and recovery codes', basics, doc(
    p('A password on its own is easy to lose control of. It can be guessed, reused from a site that was breached, or typed into a convincing fake page. ', b('Two-factor sign-in'), ' adds a second step: after your password, a six-digit code from an app on your phone. The code changes every 30 seconds and only your phone can make it, so someone who has your password still cannot get in.'),
    p(b('Recovery codes'), ' are the other half. They are your spare keys: a few single-use codes you keep somewhere safe, which get you in if you lose your phone or forget your password. Every account with a password gets them when it is made, whether or not two-factor is on.'),
    p('This page shows how to turn two-factor on, what recovery codes are for and where to keep them, and what to do if you lose your phone.'),
    panel('info', p(b('Sign in with single sign-on?'), ' Then your organization’s own sign-in handles two-factor, and your profile says so. You do not have recovery codes in Tesria, because you have no Tesria password to recover.')),

    h(2, 'Turning on two-factor'),
    p('You need a phone with an ', b('authenticator app'), ': a free app that makes these codes. Tesria suggests Aegis, 1Password, Google Authenticator or Authy, and any app that shows six-digit codes that change every 30 seconds will do. Install one first if you do not have one.'),
    step(1, 'Open your profile'),
    p('Choose your name or picture at the right of the top bar, and find the ', b('Two-factor sign-in'), ' card.'),
    ...(await picture(twoFactor, 'two-factor-section', 'The Two-factor sign-in card on the profile, with Set up two-factor boxed', 'Two-factor sign-in on your profile, while it is off.')),
    step(2, 'Choose Set up two-factor'),
    p('If you signed in more than a few minutes ago, type your current password first; otherwise leave it empty. Then choose ', b('Set up two-factor'), '.'),
    step(3, 'Scan the QR code with your app'),
    p('In the authenticator app, choose to add an account and point the phone’s camera at the square code. The app adds your Tesria and starts showing codes for it.'),
    p('Cannot scan it, say because you are on the phone itself? Under the code, ', i('Can’t scan? Enter this key by hand'), ' shows a key you can type into the app instead.'),
    ...(await picture(twoFactor, 'two-factor-enroll', 'The setup step: a QR code, the six-digit code box and Turn on', 'Scan the code, type the six digits your app shows, then Turn on. (The code and key are blurred in this picture: they are secret.)')),
    step(4, 'Type the six-digit code and choose Turn on'),
    p('Type the code the app shows now into ', b('Six-digit code'), ' and choose ', b('Turn on'), '. That proves the app and Tesria agree. The card then says ', b('On.')),
    p('Turning it on signs out your account on every other device. That is deliberate: anyone else holding a session on your account has to get past the new second step too.'),
    panel('success', p(b('Check your recovery codes now,'), ' while you are on this page: the ', b('Recovery codes'), ' card below should say you have some. They are what gets you in if the phone is lost.')),
    p('From now on, signing in asks for a code after your password. See ', pageLink('Signing in'), '.'),

    h(2, 'Recovery codes'),
    p('You get 8 recovery codes, each looking like ', c('XXXX-XXXX-XXXX'), '. Each works ', b('once'), ', in two places:'),
    ul(
      li(p(b('At the second step of signing in,'), ' in place of the code from your phone.')),
      li(p(b('On the password reset page,'), ' in place of an email link, to choose a new password. See ', pageLink('Resetting a password'), '.')),
    ),
    p('They work even if your Tesria cannot send email, which on a self-hosted server is common. Letters that are easy to confuse, such as O and 0, are never used, and dashes and capitals do not matter when you type one.'),
    h(3, 'Where to keep them'),
    p('Tesria shows your codes when you create your account and whenever you make a new set, and never again: it keeps only a scrambled copy it can check against. So keep them yourself, somewhere that is ', b('not your phone'), ' (the phone is what they replace) and not in Tesria (you cannot open it when you need them). Good places:'),
    ul(
      li(p(b('A password manager,'), ' in the entry for your Tesria. ', b('Copy'), ' makes this easy.')),
      li(p(b('Printed or written on paper,'), ' kept with other important papers. ', b('Download'), ' saves them as a text file to print.')),
    ),
    panel('warning', p(b('Treat them like a password.'), ' Anyone who has one of your codes and knows your email can reset your password. Do not email them to yourself or keep them in a shared document.')),
    h(3, 'Checking and renewing them'),
    p('The ', b('Recovery codes'), ' card on your profile says how many unused codes you have left, and warns you in red when 2 or fewer remain.'),
    ...(await picture(twoFactor, 'recovery-codes-section', 'The Recovery codes card, with Generate new codes boxed', 'How many codes are left, and the button that makes a new set.')),
    p('Choose ', b('Generate new codes'), ' (with your password if you signed in more than a few minutes ago) to make a fresh set of 8. The old ones stop working at once, so make a new set after using a few, or if you think someone has seen them. Save the new ones, tick ', b('I have saved these codes somewhere safe'), ' and choose ', b('Done'), '.'),
    p('If your account has no codes, or you never confirmed saving them, Tesria asks you after you sign in, in a box headed ', b('Set up account recovery'), ' or ', b('Do you have your recovery codes?'), '. ', b('Not now'), ' puts it off until you next sign in.'),

    h(2, 'If you lose your phone'),
    p('A lost, broken or replaced phone takes your authenticator app with it. Here is how to get going again:'),
    step(1, 'Sign in with a recovery code'),
    p('Sign in with your password as usual. At ', b('One more step'), ', type a recovery code instead of the six digits.'),
    step(2, 'Turn two-factor off'),
    p('On your profile, the ', b('Two-factor sign-in'), ' card has ', b('Current password'), ' and ', b('Or a code from the app'), '. Type your password and choose ', b('Turn off'), '.'),
    step(3, 'Set it up again on the new phone'),
    p('Choose ', b('Set up two-factor'), ' and follow the steps above with the new phone. While you are there, make a new set of recovery codes if you have used several.'),
    p('Moving to a new phone you already have in hand works the same way: turn two-factor off, then set it up again with the new phone.'),
    h(3, 'Lost the phone and every recovery code?'),
    p('Ask an administrator. They can choose ', b('Turn off two-factor'), ' for your account in ', ...adminAt('Users'), '. You are signed out everywhere, every administrator is told, and you sign in with your password alone until you set two-factor up again. They should make sure they are really talking to you before they do it.'),
    panel('note', p(b('Nobody can do this for the owner.'), ' The owner’s two-factor can only be turned off from their own profile, and only the owner can turn off another administrator’s. If you own your Tesria, keep your recovery codes especially safe.')),

    h(2, 'When two-factor is required'),
    p('An administrator can require two-factor for everyone with administrator rights. If that applies to you, the administration pages ask you to set it up before they open, and your profile does not offer to turn it off: it says ', i('Administrators on this instance must keep two-factor sign-in on'), '. To move to a new phone, sign in with a recovery code and ask the owner to turn your two-factor off in ', ...adminAt('Users'), ', then set it up again straight away.'),
  ))

  // =================================================== Resetting a password
  await page('Resetting a password', basics, doc(
    p('Forgotten your password? You can choose a new one without anyone else seeing it. Tesria offers up to three ways, because not every Tesria can send email: a server run on your own network often has no email set up at all.'),
    ul(
      li(p(b('An email link,'), ' if your Tesria sends email. The easiest, if it is offered.')),
      li(p(b('A recovery code,'), ' one of the codes you saved when you created your account. Works everywhere.')),
      li(p(b('A link from an administrator,'), ' if you have neither.')),
    ),
    p('All three start from ', b('Forgot your password?'), ' on the sign-in page.'),
    panel('info', p(b('Sign in with single sign-on?'), ' Then you have no Tesria password, and your password is reset through your organization, not here.')),

    h(2, 'By email'),
    p('Where email is set up, the reset page offers it first.'),
    step(1, 'Ask for a link'),
    p('Enter your email address and choose ', b('Email me a reset link'), '. The page answers the same whether or not the address has an account, so nobody can use it to find out who does.'),
    step(2, 'Open the email'),
    p('The message is headed ', i('Reset your password'), '. Its link works ', b('once'), ' and expires ', b('an hour'), ' after it was sent, so use it soon. No email after a few minutes? Check your spam folder, then ask again.'),
    step(3, 'Choose a new password'),
    p('The link opens ', b('Choose a new password'), '. Type it twice, at least 8 characters, and choose ', b('Reset password'), '.'),

    h(2, 'With a recovery code'),
    p('If the page offers email, choose ', b('Use a recovery code instead'), ' under the form; otherwise the page opens here.'),
    step(1, 'Enter your email address and one recovery code'),
    p('Any one of your unused codes will do. Dashes and capitals do not matter.'),
    ...(await picture(reset, 'recover', 'The Reset your password form, with the Recovery code box boxed', 'The recovery code goes in the second box.')),
    step(2, 'Choose a new password'),
    p('Type it in ', b('New password'), ' and again in ', b('Confirm new password'), ', then choose ', b('Reset password'), '. That code is now used up, so cross it off your list.'),
    p('Your profile shows how many codes you have left. If you are running low, make a new set: see ', pageLink('Two-factor and recovery codes'), '.'),

    h(2, 'From an administrator'),
    p('No email and no recovery codes? The page says ', i('Lost your codes? Ask an administrator to issue a reset link.')),
    p('An administrator chooses ', b('Reset password'), ' beside your name in ', ...adminAt('Users'), ' and gets a one-time link. It works once, for an hour, and opens straight on ', b('Choose a new password'), '.'),
    panel('warning', p(b('The link is as good as a password for that hour.'), ' Administrators: hand it over in person, or by a message you know only that person can read. Everyone else: if someone you do not know sends you a reset link, do not use it; ask your administrator.')),

    h(2, 'Afterwards'),
    p('Tesria shows ', b('Password reset'), ' with a ', b('Sign in'), ' button. You are not signed in automatically; sign in with your new password.'),
    ul(
      li(p(b('Every other device is signed out.'), ' If someone else was using your account, they are out.')),
      li(p(b('Any lock is cleared,'), ' if too many wrong tries had locked your account.')),
      li(p(b('Two-factor stays on,'), ' if you had it. You still need your phone, or another recovery code, at the second step.')),
    ),
    p('Too many reset attempts for one address in a short time (10 in a quarter of an hour) are refused with ', b('Too many attempts. Try again later.'), ' Wait a little and try again.'),
    p('To change a password you still know, use your profile instead: see ', pageLink('Password'), '.'),
  ))

  // ================================================ Finding your way around
  await page('Finding your way around', basics, doc(
    p('Every screen in Tesria is built from the same few parts: the ', b('top bar'), ' across the top, and inside a space, the ', b('sidebar'), ' on the left with the space’s pages, and the page itself with a bar of actions above it. Once you know where each is, everything else is a click away.'),
    ...(await picture(around, 'page-layout', 'A page in Tesria, with the top bar, the space sidebar and the page', 'The top bar, the sidebar with the space’s page tree, and the page with its bar of actions above it.')),

    h(2, 'The top bar'),
    p('It is on every screen. From left to right:'),
    ul(
      li(p(b('The logo'), ' takes you back to the list of spaces.')),
      li(p(b('Spaces'), ' lists every space you can see.')),
      li(p(b('Admin'), ' opens Administration, if your role gives you any administrative right. ', b('Invite people'), ' is here instead if you may invite people but not administer. In a narrower window these move into a ', b('More'), ' menu.')),
      li(p(b('Search pages…'), ' searches every page you can read. See ', b('Search'), ', below.')),
      li(p(b('The sun, moon or screen button'), ' sets light or dark and the accent color. See ', pageLink('Theme and accent'), '.')),
      li(p(b('The bell'), ' shows your notifications.')),
      li(p(b('Your picture and name'), ' open your profile: your password, two-factor, email settings and more. See ', pageLink('Your profile'), '.')),
      li(p(b('Sign out'), ' ends this session.')),
    ),
    p('On a phone, the ☰ button at the left of the top bar opens these as a menu, together with the page tree of the space you are in. See ', pageLink('The phone top bar and menu'), '.'),

    h(2, 'Search'),
    p('Search finds pages by what is in them, across every space you can see. Type a word or two into ', b('Search pages…'), ' and press ', b('Enter'), '.'),
    p('Each result shows the page’s title, its space, and the passage that matched, with your words in bold. Only published pages you are allowed to read are found; nothing else is ever shown. ', pageLink('Search'), ' explains how to search for an exact phrase or leave a word out.'),
    panel('success', p(b('Search or filter?'), ' Search looks inside the text of pages in every space. The filter at the top of the page tree (below) looks only at the titles in the space you are in, and answers as you type. When you know roughly what a page is called, the filter is quicker.')),

    h(2, 'The space sidebar'),
    p('Inside a space, the sidebar holds the space’s icon and name, ', b('+ New page'), ', the tree of its pages, and ', b('Space settings'), ' at the bottom. The name and buttons stay put while the tree scrolls, so they are always to hand however long the tree is.'),
    ...(await picture(around, 'sidebar', 'The space sidebar, with the hide button boxed and an arrow at its right edge', 'The boxed button hides the sidebar. Its right edge is a handle you drag to make it wider.')),
    h(3, 'Hiding it'),
    p('The button beside the space’s name hides the sidebar, to give the page more room. A narrow strip stays at the left with the same button, to bring it back. Tesria remembers your choice on this device.'),
    h(3, 'Making it wider or narrower'),
    p('Long titles, numbered trees and deeply nested pages take room. You can make the sidebar as wide as you like, within reason:'),
    ul(
      li(p(b('Drag its right edge.'), ' Point at the line between the sidebar and the page; it turns into a colored line and the pointer into a resize arrow. Drag it left or right.')),
      li(p(b('Double-click the edge'), ' to put the sidebar back to its usual width.')),
      li(p(b('From the keyboard,'), ' press Tab until the edge is selected, then the left and right arrow keys make it narrower or wider a step at a time. Home puts it back.')),
    ),
    p('It can be between 200 and 560 pixels wide, and never more than half the window, so the page always keeps the larger share. Tesria remembers the width on this device.'),

    h(2, 'Filtering the page tree'),
    p('A space soon has more pages than fit on the screen. The ', b('Filter pages'), ' box at the top of the tree narrows it to the pages whose titles contain what you type, so you can find one without scrolling or opening parents one by one.'),
    step(1, 'Type part of a title'),
    p('Click ', b('Filter pages'), ' and type a few letters, such as ', i('meeting'), '. The tree shrinks as you type, and the matching words are highlighted. Capitals and accents do not matter. In a numbered tree you can type a page’s number, such as ', i('2.3'), ', too.'),
    ...(await picture(around, 'tree-filter', 'The page tree filtered by meeting, with the show the pages under each match button boxed', 'Filtered by “meeting”. Meeting notes matches; the page above it is shown dimmed, to place it; the meetings under it show because the boxed button is on.')),
    p('Besides the matches, the tree shows:'),
    ul(
      li(p(b('The pages above each match,'), ' dimmed, so you can see where it lives.')),
      li(p(b('The pages under each match,'), ' while the button beside the box is on. Type the name of a section and you see the section and everything in it.')),
    ),
    step(2, 'Open a result'),
    p('Click a page to open it, or press ', b('Enter'), ' to open the first match. The filter stays as it is, so the next result is one click away: open one, read it, open the next.'),
    step(3, 'Clear it'),
    p('Press ', b('Escape'), ' in the box, or delete what you typed, to see the whole tree again. If nothing matches, the tree says ', b('No pages match.')),
    h(3, 'The pages under each match'),
    p('The button with the small tree on it, beside the box, is ', b('Show the pages under each match'), '. It is on unless you turn it off, and shows in the accent color while it is on. Turn it off when you want the matching pages alone. Tesria remembers the setting on this device.'),
    h(3, 'How long the filter lasts'),
    p('The filter stays while you move from page to page, and even if you reload, until you clear it or close the browser tab. Each space keeps its own, so filtering one space does not affect another. On a phone, the filter in the ☰ menu keeps what you typed the same way.'),
    p('While you are reordering pages with the pencil beside ', b('Pages'), ', the filter is set aside and the whole tree shows, so nothing is hidden while you move things. See ', pageLink('The page tree and reordering'), '.'),

    h(2, 'Numbered and bulleted trees'),
    p('A space can show numbers beside its pages, like the contents of a book: 1, 1.1, 1.2, 2. It makes a long tree easier to scan and to talk about (“it’s in 3.2”), which is why these docs use it. Or it can show bullets, which change shape with each level, or nothing at all.'),
    p('It is a setting of the space, so everyone sees the same. The numbers are only drawn beside the titles: they are not part of any title or address, and they follow the tree when pages are added or moved, so nobody ever has to renumber anything.'),
    p('To change it, open ', b('Space settings'), ' at the bottom of the sidebar. On the ', b('Details'), ' tab, under ', b('Page tree'), ', choose ', b('Plain'), ', ', b('Numbered'), ' or ', b('Bulleted'), '. The change is saved as soon as you choose; there is no Save button.'),
    ...(await picture(around, 'tree-style', 'The Page tree setting, with Numbered boxed', 'The Page tree setting in Space settings. Each choice shows what the tree will look like.')),
    p('You need to be able to change the space’s settings. If you cannot, ask whoever looks after the space.'),

    h(2, 'The page bar'),
    p('Above every page is a bar with what you can do to it:'),
    ul(
      li(p(b('Edit'), ', if you may edit the page.')),
      li(p(b('Full width'), ', to let the page use the whole window, for wide tables and diagrams. It changes the page for everyone who reads it. Not on phones.')),
      li(p(b('⋮'), ', for everything else.')),
    ),
    ...(await picture(around, 'page-menu', 'The ⋮ menu of a page, open, with its button boxed', 'The ⋮ menu. What it offers depends on the space and on your role.')),
    p('The ⋮ menu is where you export a page as Markdown, HTML or PDF, watch it to be told when it changes, save it as a template, move or copy it, and delete it. ', pageLink('Page actions'), ' explains each.'),
    p('Above the title, the ', b('breadcrumb'), ' shows where the page sits in its space: click any part of it to go up. Below the page are four tabs: ', b('Comments'), ', ', b('Attachments'), ', ', b('History'), ' and ', b('Restrictions'), '.'),

    h(2, 'Trust this device'),
    p('If your browser says ', i('Not secure'), ' beside the address, or you had to click past a warning to open Tesria, your server makes its own security certificate and this device does not trust it yet. The connection is still encrypted, but the warning is worth fixing, once per device.'),
    p('Your profile then has a ', b('Trust this device'), ' card with ', b('Set up this device'), ', and the sign-in page has a ', b('Trust this device'), ' link. Both open a short guide that takes about three minutes. See ', pageLink('Trusting the local certificate'), '. If your Tesria has a certificate from the internet, neither appears, and you have nothing to do.'),
  ))

  // =================================================== The tour and tips
  await page('The tour and tips', basics, doc(
    p('Tesria introduces itself in two ways. The ', b('tour'), ' is five short screens the first time you sign in, each with a short moving picture, covering the ideas the rest of Tesria is built on. ', b('Tips'), ' come later, one at a time, as you use Tesria: small cards that point out something useful at the moment it would help.'),
    p('Both are there to help you, not to get in the way. You can skip the tour, turn tips off, and ask for either again at any time.'),

    h(2, 'The tour'),
    p('It opens the first time you sign in. Its five screens are:'),
    ol(
      li(p(b('Spaces and pages:'), ' how Tesria is organized.')),
      li(p(b('Writing:'), ' creating a page, and drafts.')),
      li(p(b('Working together:'), ' editing at the same time, comments and mentions.')),
      li(p(b('Finding things:'), ' search and labels.')),
      li(p(b('You:'), ' your profile, and where tips are turned off.')),
    ),
    ...(await picture(tour, 'tour', 'The first screen of the tour, with Skip the tour boxed', 'The tour’s first screen. Next moves on; Skip the tour ends it.')),
    p(b('Next'), ' and ', b('Back'), ' move through it, and ', b('Done'), ' on the last screen finishes it. ', b('Skip the tour'), ' ends it at once. Closing the tab, or leaving it any other way, counts as skipping, so it does not come back by itself.'),
    p('On the last screen, ', b('Show me tips as I go'), ' is ticked. Leave it ticked to get tips, or untick it before you choose ', b('Done'), ' if you would rather not.'),

    h(2, 'Tips'),
    p('A tip is a small card near the bottom of the screen, such as ', i('Type / for anything'), ' in the editor, or ', i('Drag pages to rearrange them'), ' once a space has a few pages. Many have a short moving picture showing how.'),
    p('Tips are deliberately rare:'),
    ul(
      li(p('One at a time, at most three a day, and each one only once.')),
      li(p('Never over a dialog or an open menu, and never while you are selecting text in the editor.')),
      li(p('Only about something on the screen in front of you.')),
    ),
    p('Each tip has two buttons. ', b('Got it'), ' puts that tip away for good. ', b('Turn off tips'), ' stops them all; for a few seconds afterwards, ', b('Undo'), ' turns them back on if you did not mean it.'),

    h(2, 'Changing your mind'),
    p('Your profile has a ', b('Tour and tips'), ' card with everything in one place:'),
    ...(await picture(tour, 'tour-and-tips', 'The Tour and tips card on the profile, with Show the tour again boxed', 'Tour and tips on your profile. Here tips are turned off.')),
    ul(
      li(p(b('Show tips as I go'), ' turns tips on or off.')),
      li(p(b('Show the tour again'), ' opens the tour now.')),
      li(p(b('Reset dismissed tips'), ' brings back the tips you put away with Got it. The number beside it says how many there are.')),
    ),
  ))

  // ==================================================== Theme and accent
  await page('Theme and accent', basics, doc(
    p('You can choose how Tesria looks: ', b('light'), ' or ', b('dark'), ', or following your computer or phone, and an ', b('accent color'), ' for buttons, links and highlights. A dark theme is easier on the eyes at night; following the system switches for you when your device does. If you use more than one Tesria, a different accent on each tells them apart at a glance.'),

    h(2, 'Changing it'),
    step(1, 'Open the appearance menu'),
    p('Choose the button at the right of the top bar, just left of the bell. It shows a sun, a moon or a screen, depending on the theme in use.'),
    ...(await picture(theme, 'theme-menu', 'The appearance menu open under its button in the top bar', 'The boxed button opens the menu. Here the theme is Light and the accent Blue.')),
    step(2, 'Choose a theme'),
    ul(
      li(p(b('System'), ' follows your computer or phone, light by day and dark by night if your device switches. This is where everyone starts, and the menu shows which one your device is using now.')),
      li(p(b('Light'), ' is always light.')),
      li(p(b('Dark'), ' is always dark.')),
    ),
    step(3, 'Choose an accent color'),
    p('Pick one of the round swatches: Blue, Teal, Green, Purple, Orange or Magenta. If your organization has its own brand color, it comes first, under your organization’s name.'),
    p('Each choice takes effect at once; there is nothing to save. Close the menu with its ', b('×'), ', or click anywhere else.'),

    h(2, 'Where it is kept'),
    p('Your choice is kept in this browser, not in your account, so your phone and your laptop can look different, and it works even when you are not signed in. If you clear the browser’s data for your Tesria, it goes back to the start: System and your organization’s usual accent.'),

    h(2, 'When the choice is made for you'),
    p('An administrator can decide some of this for everyone, in ', ...adminAt('Branding'), ':'),
    ul(
      li(p(b('The theme'), ' can be fixed to light only or dark only. Then the menu offers no theme.')),
      li(p(b('The accent'), ' can be used for everyone. Then the menu offers no accent. Or the administrator can simply choose the one people start with, which you can still change.')),
    ),
    p('If both are fixed, there is nothing to choose, and the button is not in the top bar at all. See ', pageLink('Branding'), '.'),
  ))
}

// Pictures these pages no longer use: the first version's phone views, and
// the top bar close-up, which the whole-page picture replaced. Taken down so
// they do not linger in the pages' attachments or in the exported pack.
const RETIRED = {
  'Signing in': ['sign-in.phone.png'],
  'Accounts and invites': ['register-invited.phone.png'],
  'Two-factor and recovery codes': ['two-factor-section.phone.png', 'recovery-codes-section.phone.png'],
  'Resetting a password': ['recover.phone.png'],
  'Finding your way around': ['page-layout.phone.png', 'top-bar.png', 'top-bar.phone.png', 'page-menu.phone.png'],
  'The tour and tips': ['tour-and-tips.phone.png'],
  'Theme and accent': ['theme-menu.phone.png'],
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/DOCS')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = find(tree, title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
