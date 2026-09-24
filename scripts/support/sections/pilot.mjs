// The pilot of the Support rewrite (dev-plan 15.6): one page of each kind,
// written to the owner's rules from the review of the first version, for the
// owner to approve before the rest of the site follows.
//
//   Trusting the local certificate   a procedure (Installation and operations)
//   Panels                           an element page (User manual → The editor → Elements)
//   Creating a space                 a manual page (User manual → Spaces)
//   Opening Tesria by name           a topic page (Installation and operations)
//   Templates                        a manual page (User manual → Pages)
//
// Run it on its own: scripts/support/publish-support.sh pilot
// It is not in the default list, so the first version's sections do not
// overwrite these pages, and these do not touch any other page.

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

// Pictures only where they show something words cannot, such as where a
// control is, and taken in a narrow window: at about the width they are
// shown, their text is the size of the page's on a desktop and still
// readable on a phone (the owner, 2026-09-23).
const NARROW = { width: 480, height: 900 }
const ADDRESS = [{ wait: 1500 }, { type: 'wiki-server.local', selector: '#trust-address' }, { click: '[data-device="mac"]' }, { wait: 300 }]
const SPACE_FORM = [
  { wait: 2000 }, { click: '.row-gap .btn--primary' }, { wait: 400 },
  { type: 'TEAM', selector: 'form.card.form-inline label:nth-of-type(1) input' },
  { type: 'Team handbook', selector: 'form.card.form-inline label:nth-of-type(2) input' },
  { type: 'How we work, in one place', selector: 'form.card.form-inline label:nth-of-type(3) input' },
  { eval: 'document.activeElement && document.activeElement.blur()' },
  // The crop leaves room around the form for its labels; what is behind
  // that room (the heading, the space cards) is hidden rather than cut in half.
  { css: '.row-between, .space-grid { visibility: hidden !important; }' },
]

// Marks the menu's Save as template button, so a picture can box it.
const TAG_SAVE_TEMPLATE = "[...document.querySelectorAll('.overflow-menu__dropdown button')].find((b) => b.textContent.trim() === 'Save as template')?.setAttribute('data-shot', 'save-template')"

/**
 * Tesria Demo needs a template for the "Start from a template" picture: the
 * menu only appears when there is one. Made once, if it is missing.
 */
export async function prepare({ lib, author }) {
  const space = await author.call('GET', '/api/spaces/DEMO')
  const have = await author.call('GET', `/api/templates?spaceId=${space.id}`)
  if (have.some((t) => t.name === 'Meeting notes' && t.spaceId === space.id)) return {}
  const { doc, h, p, text, bold, panel, ul, li, tasks, task } = lib
  const content = doc(
    panel('info', p(text('Replace the hints in each section, then delete this box.', bold))),
    h(2, 'Attendees'), ul(li(p('Who was there?'))),
    h(2, 'Agenda'), ul(li(p('What will be discussed?'))),
    h(2, 'Decisions'), ul(li(p('What was agreed, and by whom?'))),
    h(2, 'Action items'), tasks(task(false, 'Who does what, by when?')),
  )
  await author.call('POST', '/api/templates', { spaceId: space.id, name: 'Meeting notes', description: 'Attendees, agenda, decisions and action items.', contentJson: JSON.stringify(content) })
  console.log('  made the Meeting notes template in Tesria Demo')
  return {}
}

export const shots = ({ demo }) => [
  // ---- Trusting the local certificate: the two controls in the /trust
  // guide a reader has to find. Its written steps are not pictured: the
  // page says the same in words, which read at any size (the owner,
  // 2026-09-23).
  {
    name: 'trust-device', url: '/trust', viewport: NARROW, phone: false, steps: ADDRESS,
    clipTo: '[data-step="1"]', clipPad: 12,
    annotate: [{ type: 'box', target: '.trust-device.is-active', pad: 5 }],
  },
  {
    name: 'trust-address', url: '/trust', viewport: NARROW, phone: false, steps: ADDRESS,
    clipTo: '[data-step="2"]', clipPad: 12,
    annotate: [{ type: 'box', target: '.trust-address', pad: 5 }],
  },

  // ---- Creating a space: where the button is, and the form.
  {
    name: 'space-new', url: '/spaces', viewport: NARROW, phone: false, settle: 800, steps: [{ wait: 2000 }],
    clipTo: ['.topbar', '.row-between'], clipPad: 0,
    annotate: [{ type: 'box', target: '.row-gap .btn--primary', pad: 5 }],
  },
  {
    name: 'space-form', url: '/spaces', viewport: NARROW, phone: false, steps: SPACE_FORM,
    clipTo: 'form.card.form-inline', clipPad: 40,
    annotate: [
      { type: 'box', target: 'form.card.form-inline label:nth-of-type(1)', pad: 5 },
      { type: 'box', target: 'form.card.form-inline label:nth-of-type(2)', pad: 5 },
      { type: 'box', target: 'form.card.form-inline button[type="submit"]', pad: 5 },
      { type: 'note', target: 'form.card.form-inline label:nth-of-type(1)', label: 'Cannot be changed later', dy: -30 },
    ],
  },

  // ---- Templates: where Save as template is, its form, and where a new
  // page offers one. From a meeting page in Tesria Demo, whose space has a
  // Meeting notes template (prepare, below).
  {
    name: 'template-menu', url: demo('Kickoff, September 2'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: TAG_SAVE_TEMPLATE }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '[data-shot="save-template"]', pad: 4 }],
  },
  {
    name: 'template-form', url: demo('Kickoff, September 2'), viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: TAG_SAVE_TEMPLATE },
      { click: '[data-shot="save-template"]' }, { wait: 300 },
      { type: 'Meeting notes', selector: '.template-form input' },
      { eval: 'document.activeElement && document.activeElement.blur()' },
    ],
    clipTo: '.overflow-menu__dropdown', clipPad: 8,
    annotate: [{ type: 'box', target: '.template-form', pad: 4 }],
  },
  {
    name: 'template-pick', url: '/spaces/DEMO/new', viewport: NARROW, phone: false,
    waitFor: '.editor-form select', steps: [{ wait: 1500 }],
    clipTo: ['.editor-form label.change-comment'], clipPad: 12,
    annotate: [{ type: 'box', target: '.editor-form label.change-comment select', pad: 4 }],
  },
  // Closes that new page without saving, which discards its draft.
  { name: 'template-pick-closed', settle: 300, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },

  // ---- Panels: an animation of adding one and changing its type, in a
  // narrow window so it reads on a phone, and shown at its own size. It
  // opens a new page in Tesria Demo; the harness discards the draft after.
  {
    name: 'panel-insert', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
    waitFor: '.ProseMirror', lead: 900, tail: 1400,
    css: '.tip, .onboarding-tip { display: none !important; }',
    steps: [
      { click: '.ProseMirror' }, { wait: 400 },
      { typeSlowly: '/info', delay: 110 }, { wait: 900 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 300 },
      { typeSlowly: 'The office is closed on Monday.', delay: 45 }, { wait: 1100 },
      { moveTo: '.floating-menu button[title="Warning panel"]' }, { wait: 500 },
      { click: '.floating-menu button[title="Warning panel"]' }, { wait: 1300 },
      { moveTo: '.floating-menu button[title="Tip panel"]' }, { wait: 500 },
      { click: '.floating-menu button[title="Tip panel"]' }, { wait: 800 },
    ],
  },
]

export async function build({ top, page, ensure, attachCurrent, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, codeBlock, fileBlock, picture, phonePicture, animation }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)

  // ====================================================== Trusting the certificate
  const install = top['Installation and operations']
  const trust = await ensure('Trusting the local certificate', install)
  const sh = await attachCurrent(trust, 'trust-ca.sh', readFileSync(join(ROOT, 'deploy/scripts/trust-ca.sh')), 'text/plain')
  const ps1 = await attachCurrent(trust, 'trust-ca.ps1', readFileSync(join(ROOT, 'deploy/scripts/trust-ca.ps1')), 'text/plain')

  await page('Trusting the local certificate', install, doc(
    p('The first time you open Tesria, your browser may stop you with a warning such as ', i('Your connection is not private'), ' or ', i('This Connection Is Not Private'), '. Nothing is wrong with your server. This page explains why it happens and walks you through making it go away for good, one device at a time.'),

    h(2, 'Why the warning appears'),
    p('Every time your browser opens a secure web page, the server shows it a ', b('certificate'), ': a kind of ID card that proves the server is who it says it is. The browser only accepts ID cards issued by a short list of organizations it already trusts, called certificate authorities. Public websites get their certificates from them.'),
    p('A Tesria server on your own network cannot get one of those, because those organizations only issue certificates for addresses on the public internet. So Tesria makes its own. The connection is still fully encrypted; your browser just does not recognize who issued the ID card, so it warns you.'),
    p('The fix is to tell each device, once, that certificates from your Tesria server are to be trusted. After that the warning is gone on that device for good, for every address the server answers on.'),
    panel('info',
      p(b('Does this apply to you?'), ' Only if you open Tesria at a local address, such as ', c('wiki-server.local'), ' or ', c('localhost'), '. If you open it at a real web address, like ', c('wiki.example.com'), ', and see a padlock, your server already has a public certificate and you can skip this page.')),

    h(2, 'Before you start'),
    panel('note',
      p(b('Throughout this page, '), c('your-server'), b(' stands for your server’s address:'), ' whatever you type into the browser to open Tesria, without ', c('https://'), '. For example, if you open Tesria at ', c('https://wiki-server.local'), ', then ', c('http://your-server/trust'), ' means ', c('http://wiki-server.local/trust'), '.')),
    ul(
      li(p(b('About three minutes'), ' per device.')),
      li(p(b('Permission to change the device’s settings.'), ' On a computer that means an administrator account, because the certificate is added for everyone who uses it.')),
      li(p(b('The device on the same network as the server'), ', the same as when you use Tesria.')),
    ),

    h(2, 'The easy way: the Trust this device guide'),
    p('Tesria has a guide built in that asks which device you are on, fills your server’s address into a small script, and tells you exactly what to click. There are three ways to open it:'),
    ul(
      li(p(b('From your profile.'), ' Once you are signed in, open your profile (your initials at the top right) and choose ', b('Set up this device'), ' under ', b('Trust this device'), '.')),
      li(p(b('From the sign-in page.'), ' Choose ', b('Trust this device'), ' under ', i('Did your browser warn that this site is not secure?'), '.')),
      li(p(b('By its address.'), ' Type ', c('http://your-server/trust'), ' into the browser. Note ', c('http'), ', not ', c('https'), ': the guide is served without encryption on purpose, so a device that does not trust the server yet can open it with no warning. This is the easiest way on a phone.')),
    ),
    p('If ', c('http://your-server/trust'), ' does not open, your network or security software may block plain ', c('http'), ' addresses. Open ', c('https://your-server/trust'), ' instead and click past the warning once, as described under ', b('Getting past the warning the first time'), '.'),

    h(3, 'First, open the guide'),
    p('Open it any of the three ways above. It starts by explaining the warning, then has four numbered steps. The steps below have the same numbers, so you can follow along.'),

    step(1, 'Which device are you on?'),
    p('The guide guesses your device from your browser. If it guessed wrong, choose the right one. Everything below changes to match.'),
    ...(await picture(trust, 'trust-device', 'Choosing the device in the guide', 'The highlighted button is the device the steps are for.')),

    step(2, 'What address do you open Tesria at?'),
    p('The guide fills in the address you used to reach it. It has to be the address you normally open Tesria at, because that is the address the script will trust. If you type a numeric address such as 192.168.1.50, the guide explains how to find your computer’s name instead, since certificates are issued for names.'),
    ...(await picture(trust, 'trust-address', 'The address box in the guide', 'Your server’s address, without https://.')),

    step(3, 'Trust the certificate'),
    p('This is the part that does the work, and it differs a little by device. On a phone the guide takes you through the Settings app; see ', b('On a phone'), ' below.'),
    p(b('On a Mac or Linux,'), ' you download a small script with your address already in it and run it with one line, which the guide gives you to copy. It asks for your password, because adding a trusted certificate changes a setting for the whole computer.'),
    p(b('On Windows,'), ' there is no file to download. You copy one line into PowerShell and press Enter. Windows then asks whether to install a certificate from ', i('Caddy Local Authority'), ': that is your server, so choose ', b('Yes'), '.'),
    panel('info', p(b('Why a line and not a script on Windows?'), ' Windows refuses to run script files downloaded from the internet unless you change a security setting, and on a work computer that setting is often locked. A line you paste in yourself is not affected, and it needs no administrator, because it trusts the server for your own Windows account.')),
    panel('success', p(b('Typing your password shows nothing.'), ' In Terminal, and in most command windows, the password you type is hidden completely, not even as dots. That is normal: type it and press Return.')),

    step(4, 'Check it worked'),
    p('Quit your browser completely and open it again. Closing its windows is not always enough, because some browsers keep running in the background: in Chrome or Edge, type ', c('chrome://restart'), ' or ', c('edge://restart'), ' into the address bar; in Safari, press ', b('⌘ Q'), '. Then choose ', b('Open Tesria securely'), ' at the end of the guide. If Tesria opens with no warning and the address bar shows a padlock, you are done on this device.'),
    p('Still says “Not secure”? Check that you opened Tesria by its name, such as ', c('wiki-server.local'), ', and not a number such as 192.168.1.50. A number never matches the certificate, however it is trusted. The guide’s last step lists the other causes.'),

    h(2, 'Getting past the warning the first time'),
    p('To sign in and reach your profile before the device trusts the server, you can tell the browser to go ahead this once. It is safe here because it is your own server on your own network. ', b('Do not do this for websites you do not recognize.')),
    table([
      ['Browser', 'What to choose on the warning page'],
      ['Chrome, Edge, Brave', 'Advanced, then Proceed to your-server (unsafe)'],
      ['Safari', 'Show Details, then visit this website, then Visit Website'],
      ['Firefox', 'Advanced…, then Accept the Risk and Continue'],
    ], [200, 500]),
    p('Some browsers offer no way past, for example on a work computer your IT department manages. Use ', c('http://your-server/trust'), ' instead.'),

    h(2, 'On a phone'),
    p('On an iPhone, iPad or Android phone there is no script: you install the certificate and then switch trust on in the Settings app. Open ', c('http://your-server/trust'), ' on the phone and the guide shows the steps for it. On an iPhone or iPad, use ', b('Safari'), '; other browsers there cannot install certificates.'),
    h(3, 'iPhone and iPad'),
    ol(
      li(p('In Safari, open ', c('http://your-server/trust'), ', choose ', b('iPhone or iPad'), ', and tap ', b('Download the certificate'), '. Tap ', b('Allow'), '. It says a configuration profile was downloaded; that is the certificate.')),
      li(p('Open the ', b('Settings'), ' app. Tap ', b('Profile Downloaded'), ' near the top, then ', b('Install'), ', enter your passcode, and tap ', b('Install'), ' twice more.')),
      li(p('Go to ', b('Settings, General, About, Certificate Trust Settings'), '. It is at the very bottom of About.')),
      li(p('Switch on the certificate named ', i('Caddy Local Authority'), ' and tap ', b('Continue'), '. Without this step the certificate is installed but not trusted, and the warning stays.')),
    ),
    h(3, 'Android'),
    ol(
      li(p('Open ', c('http://your-server/trust'), ', choose ', b('Android'), ', and tap ', b('Download the certificate'), '. It is saved as ', c('tesria-ca.crt'), '.')),
      li(p('Open ', b('Settings'), ' and search for ', b('CA certificate'), '. The menus vary between phone makers; it is usually under Security and privacy, More security settings, Encryption and credentials, Install a certificate.')),
      li(p('Choose ', b('CA certificate'), ', then ', b('Install anyway'), ', and confirm with your screen lock.')),
      li(p('Pick ', c('tesria-ca.crt'), '. Android then says your network may be monitored, as it does for any certificate you add yourself.')),
    ),

    h(2, 'Doing it by hand'),
    p('If you would rather not use the guide, or you are setting up many computers, the same scripts are here. They are the ones the guide gives out, before your address is filled in.'),
    fileBlock(sh, 'player'),
    fileBlock(ps1, 'player'),
    p(c('trust-ca.sh'), ' is for Mac and Linux, and ', c('trust-ca.ps1'), ' for Windows. Each needs your server’s address, and you can give it one of two ways:'),
    ul(
      li(p(b('Edit the script.'), ' Open it in a plain text editor (TextEdit, Notepad or any code editor) and change the one marked line near the top:')),
    ),
    codeBlock('bash', '# Mac and Linux: trust-ca.sh\nTESRIA_ADDRESS="localhost"          # before\nTESRIA_ADDRESS="wiki-server.local"  # after: your address'),
    codeBlock('powershell', '# Windows: trust-ca.ps1\n$TesriaAddress = "localhost"          # before\n$TesriaAddress = "wiki-server.local"  # after: your address'),
    ul(
      li(p(b('Or give the address when you run it'), ', and leave the file alone:')),
    ),
    codeBlock('bash', '# Mac or Linux, in Terminal\nbash ~/Downloads/trust-ca.sh wiki-server.local'),
    codeBlock('powershell', '# Windows, in PowerShell\npowershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\\Downloads\\trust-ca.ps1" wiki-server.local'),
    p('On Windows, ', c('-ExecutionPolicy Bypass'), ' is what lets the script run: Windows blocks script files by default, with the message ', i('running scripts is disabled on this system'), '. This allows the one script, once, and changes no setting. If your employer has locked it, scripts cannot run at all; use the single line instead, which is what the guide gives you:'),
    codeBlock('powershell', '$c = "$env:TEMP\\tesria-ca.crt"; Invoke-WebRequest -UseBasicParsing -Uri "http://wiki-server.local/ca.crt" -OutFile $c; Import-Certificate -FilePath $c -CertStoreLocation Cert:\\CurrentUser\\Root'),
    p('That trusts the server for your Windows account. The script trusts it for everyone who uses the computer, which is why the script needs an administrator and the line does not.'),
    p('Either way, the script downloads the certificate from ', c('http://your-server/ca.crt'), ', shows its fingerprint, adds it to the device’s trusted list, and checks that ', c('https://your-server'), ' now opens without a warning.'),
    h(3, 'Just the certificate'),
    p('Some tools and devices want the certificate file itself. It is always at ', c('http://your-server/ca.crt'), ' and downloads as ', c('tesria-ca.crt'), '.'),

    h(2, 'Firefox'),
    p('Firefox keeps its own list of trusted certificates, separate from your computer’s, so it needs one more step after the others:'),
    ol(
      li(p('Download the certificate from ', c('http://your-server/ca.crt'), '.')),
      li(p('In Firefox, open ', b('Settings'), ', then ', b('Privacy & Security'), ', scroll down to ', b('Certificates'), ', and choose ', b('View Certificates…'), '.')),
      li(p('On the ', b('Authorities'), ' tab, choose ', b('Import…'), ', pick ', c('tesria-ca.crt'), ', tick ', b('Trust this CA to identify websites'), ', and choose ', b('OK'), '.')),
    ),

    h(2, 'If the warning comes back'),
    ul(
      li(p(b('Quit the browser completely'), ' and open it again. Browsers remember the old answer until they restart.')),
      li(p(b('Check the address.'), ' A device trusts the server, but a browser only stops warning when it reaches it by a name. Opening it by number, such as 192.168.1.50, can still warn.')),
      li(p(b('Was the server reinstalled or moved?'), ' If its certificate storage was deleted, for example with ', c('docker compose down -v'), ' or a move to a new machine without it, the server made a new certificate. Every device needs these steps again. Ordinary restarts and upgrades never do this.')),
    ),
  ))

  // ============================================== Opening Tesria by name
  // The owner's home network, 2026-09-23: a .local name took several tries
  // to open. Words only: routers and system files cannot be photographed
  // usefully, and every router's screens differ.
  await ensure('Opening Tesria by name', install)
  await page('Opening Tesria by name', install, doc(
    p('Once Tesria is running on a computer in your home or office, other devices reach it by that computer’s ', b('name'), ', such as ', c('studio.local'), ', or by its ', b('address'), ', a number such as 192.168.1.50. This page explains why the name is the one to use, how to find it, and what to do if the name sometimes takes a few tries to open.'),

    h(2, 'Why a name and not a number'),
    ul(
      li(p(b('The secure connection needs it.'), ' Tesria’s certificate is issued for a name. Opened by its number, the browser keeps saying “Not secure”, even on a device that trusts the server. See ', b('Trusting the local certificate'), '.')),
      li(p(b('Numbers change.'), ' Your router hands out addresses and can give the computer a different one after a restart. The name stays the same.')),
    ),

    h(2, 'Finding the server’s name'),
    p('On the computer that runs Tesria:'),
    ul(
      li(p(b('Mac:'), ' System Settings, General, Sharing. The name is under ', b('Local hostname'), ', for example ', c('studio.local'), '.')),
      li(p(b('Windows:'), ' Settings, System, About. Take the ', b('Device name'), ' and add ', c('.local'), ', for example ', c('office-pc.local'), '.')),
      li(p(b('Linux:'), ' run ', c('hostname'), ' in a terminal and add ', c('.local'), '.')),
    ),
    p('Then open ', c('https://that-name'), ' on another device. Tesria makes a certificate for whichever name it is opened by, so a device that already trusts the server needs nothing more.'),

    h(2, 'Why it sometimes takes a few tries'),
    p('A name ending in ', c('.local'), ' is not looked up in your router’s list of names. Instead the device calls out to the whole network, “who is studio?”, and waits for that computer to answer. It works without any setup, which is why every Mac and most other computers use it, but it is not always quick:'),
    ul(
      li(p(b('Wi-Fi drops some of these calls.'), ' Many routers treat messages sent to everyone as low priority, and some filter them.')),
      li(p(b('Windows asks your router first.'), ' It waits for the router to say it has never heard of the name, and only then calls out on the network, so the first try can time out.')),
      li(p(b('A sleeping computer answers late.'), ' A laptop that has dimmed its Wi-Fi to save power may miss the call entirely.')),
    ),
    p('So the name might fail once or twice and then work. None of this is Tesria: it is how the name is found. The fixes below make it instant, from the easiest to the most thorough.'),

    h(2, 'Making it reliable'),
    h(3, 'Keep the server awake, and wired if you can'),
    p('Stop the server computer from sleeping (on a Mac, System Settings, Energy, ', b('Prevent automatic sleeping when the display is off'), '), and plug it into the router with a network cable if you can. A wired, awake computer answers every call at once.'),

    h(3, 'Check your router’s settings'),
    p('Two settings on most routers affect those network-wide calls. Their names and places differ from router to router; on TP-Link Archer routers they are under ', b('Advanced'), '.'),
    ul(
      li(p(b('AP isolation'), ' (sometimes “client isolation”) stops devices on Wi-Fi from talking to each other at all. Make sure it is ', b('off'), ' for the network your devices use. It is often on for guest networks, which is one reason Tesria cannot be reached from a guest network.')),
      li(p(b('IGMP snooping'), ' (usually under Network, IPTV or Multicast) decides which devices hear messages sent to everyone. If names are unreliable, try switching it the other way, test for a day, and keep whichever works better.')),
    ),

    h(3, 'Give the server a fixed address'),
    p('Ask the router to always give the server computer the same number. The setting is usually called ', b('Address Reservation'), ' or ', b('DHCP reservation'), '; on a TP-Link Archer it is under Advanced, Network, DHCP Server. Choose the server computer from the list of connected devices and save. This does not change how the name is found, but it is needed for the next two fixes, which point a name at a number.'),

    h(3, 'Write the name into a computer’s own list'),
    p('Every computer keeps a small list of names and numbers, called the ', b('hosts file'), ', which it checks before anything else. A line in it makes the name open instantly on that computer. Phones and tablets do not let you change theirs.'),
    p(b('On Windows:')),
    ol(
      li(p('Press the Windows key, type ', i('Notepad'), ', right-click it and choose ', b('Run as administrator'), '.')),
      li(p('Choose ', b('File, Open'), ', go to ', c('C:\\Windows\\System32\\drivers\\etc'), ', change the file type from Text Documents to ', b('All Files'), ', and open ', c('hosts'), '.')),
      li(p('Add a line at the end with the server’s fixed address and its name, then save:')),
    ),
    codeBlock('text', '192.168.1.50   studio.local'),
    p(b('On a Mac or Linux:'), ' open Terminal and run ', c('sudo nano /etc/hosts'), '. Type your password, add the same line at the end, then press Control-O, Return and Control-X to save and close.'),
    panel('warning', p(b('Keep it up to date.'), ' If the server’s address ever changes, this line points at the wrong computer and the name stops working on that device. That is why the fixed address above comes first.')),

    h(3, 'Run a name server for your whole network'),
    p('The most thorough fix, and the one that also works on phones: a small program on an always-on device, such as a Raspberry Pi, that answers name lookups for everyone. ', b('Pi-hole'), ' and ', b('AdGuard Home'), ' are free and popular, and both block ads as well.'),
    ol(
      li(p('Install one of them and give its device a fixed address.')),
      li(p('Add a local name for the server, pointing at the server’s fixed address. Use a name ending in ', c('.home.arpa'), ', such as ', c('wiki.home.arpa'), ': that ending is set aside for home networks. Avoid ', c('.local'), ', which belongs to the network-wide calls above and confuses both methods.')),
      li(p('In the router’s DHCP settings, set the DNS server to the address of the Pi-hole or AdGuard device, so every device on the network uses it.')),
      li(p('Open ', c('https://wiki.home.arpa'), '. Tesria makes a certificate for the new name, and devices that trust the server trust it for this name too.')),
    ),

    h(2, 'Once you have settled on a name'),
    ul(
      li(p('Put it in ', b('Admin, Settings, Public address'), ', such as ', c('https://wiki.home.arpa'), '. Links in emails use it, and the ', b('Trust this device'), ' guide suggests it to anyone who opens Tesria by number.')),
      li(p('Tell everyone the new address, and bookmark it on each device.')),
    ),
  ))

  // ============================================================ Templates
  // The owner could not find how to make a template (2026-09-23): it was one
  // paragraph inside Creating a page.
  const pagesSection = await ensure('Pages', top['User manual'])
  const templates = await ensure('Templates', pagesSection)
  await page('Templates', pagesSection, doc(
    p('A ', b('template'), ' is a page that new pages start from. Instead of a blank page, whoever creates one gets your headings, your tables and your hints already in place, and only has to fill them in. Templates keep pages that should look alike looking alike, and save everyone from copying the last one and deleting its contents.'),
    p('Good candidates are pages your team writes again and again:'),
    ul(
      li(p(b('Meeting notes:'), ' attendees, agenda, decisions, action items.')),
      li(p(b('Project brief:'), ' goal, people, timeline, open questions.')),
      li(p(b('Incident report:'), ' what happened, impact, cause, what changes now.')),
      li(p(b('How-to guide:'), ' what you will need, the steps, and what to do if it goes wrong.')),
    ),

    h(2, 'Making a template'),
    p('A template is made from a page, so start by writing one.'),
    step(1, 'Write the page new ones should start as'),
    p('Put in the headings, tables and lists every page of this kind needs, and write hints where the details go, such as “Owner: who?” or “What was agreed, and by whom?”. An ', b('Info panel'), ' at the top is a good place for instructions the writer should delete once they have filled the page in. You can save the page as usual, or keep it as a draft.'),
    step(2, 'Choose Save as template'),
    p('On that page, open the ', b('⋮'), ' menu at the top right and choose ', b('Save as template'), '.'),
    ...(await picture(templates, 'template-menu', 'The page menu with Save as template', 'Save as template is in the page’s ⋮ menu.')),
    step(3, 'Name it and choose where it is offered'),
    p('Give the template a name people will recognize when they create a page, such as ', i('Meeting notes'), '. Then choose where it is offered, and choose ', b('Save'), '.'),
    ...(await picture(templates, 'template-form', 'Naming a template', 'The name, and where the template is offered.')),
    ul(
      li(p(b('This space only'), ' offers it when someone creates a page in this space. Most templates belong here.')),
      li(p(b('Instance-wide'), ' offers it in every space. Use it for something the whole organization shares, such as an incident report.')),
    ),
    p('The template is a copy of the page as it is now. Changing the page later does not change the template; save it as a template again if you want the new version.'),

    h(2, 'Starting a page from a template'),
    p('Create a page as usual, with ', b('+ New page'), '. Above the title, ', b('Start from a template (optional)'), ' lists the space’s templates and the instance-wide ones. Choose one and the page fills in with it; then give it a title and write.'),
    ...(await picture(templates, 'template-pick', 'Choosing a template for a new page', 'Start from a template appears above the title of a new page.')),
    p('This menu only appears when there is at least one template to offer. If you have already written something, Tesria asks first, because the template replaces everything on the page so far.'),

    h(2, 'Renaming and deleting templates'),
    p('Every template offered in a space is listed in ', b('Space settings, Templates'), ', the space’s own first, then the instance-wide ones. ', b('Rename'), ' changes its name and description; ', b('Delete'), ' stops it being offered. Pages already made from it are not changed either way.'),
    p('Who may rename or delete one:'),
    ul(
      li(p(b('A space’s template:'), ' anyone who can edit that space.')),
      li(p(b('An instance-wide template:'), ' whoever made it, and administrators.')),
    ),
  ))

  // ========================================================= Panels (an element)
  const manual = top['User manual']
  const editorPage = await ensure('The editor', manual)
  const elements = await ensure('Elements', editorPage)
  const panels = await ensure('Panels', elements)
  await page('Panels', elements, doc(
    p('A panel is a colored box around one or more paragraphs. It tells readers “stop and read this” before they have read a word of it, and its color tells them what kind of thing it is: background, a tip, a warning. Use one for the thing on a page that nobody should miss.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/info', 'An Info panel (blue)'],
      ['/note', 'A Note panel (purple)'],
      ['/tip', 'A Tip panel (green)'],
      ['/warning', 'A Warning panel (yellow)'],
      ['/error', 'An Error panel (red)'],
    ], [200, 500]),
    p('Type the command at the start of an empty line and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b('Panels'), '. If you select some text first, either way puts that text inside the new panel.'),
    ...(await animation(panels, 'panel-insert', 'Typing /info makes a panel; the buttons above it change its type.')),

    h(2, 'The five kinds, and when to use each'),
    p('Pick the kind by what the text means, not by the color you like: readers learn what each color means, and a warning in a green box reads as good news.'),
    panel('info', p(b('Info: background worth knowing.'), ' Use it for context that helps but is not an instruction. For example: ', i('This page describes the release process as of version 2.'), ' Or: ', i('The figures below come from the finance team’s March report.'))),
    panel('note', p(b('Note: something to keep in mind.'), ' A detail that is easy to overlook and changes how you read the rest. For example: ', i('Everything here applies to the Berlin office as well, except where a step says otherwise.'))),
    panel('success', p(b('Tip: a better way to do it.'), ' A shortcut, a best practice, or a trick that saves time. For example: ', i('Press Ctrl+K to search from anywhere, without reaching for the mouse.'))),
    panel('warning', p(b('Warning: be careful.'), ' Something can go wrong, but it can be put right. For example: ', i('Changing the build settings affects everyone on the team. Tell the channel before you do.'))),
    panel('error', p(b('Error: do not do this.'), ' Something that causes real harm or cannot be undone. For example: ', i('Never share the admin password in chat. Use the password manager.'))),

    h(2, 'Changing and removing a panel'),
    p('Click anywhere inside a panel and a small menu appears above it:'),
    ul(
      li(p(b('The five colored buttons'), ' switch the panel to that kind. Whatever is inside stays as it is.')),
      li(p(b('Remove panel'), ' takes the box away and keeps everything that was in it, as ordinary text.')),
    ),
    p('A panel can hold anything a page can: several paragraphs, lists, tables, pictures, even code. To add another paragraph inside it, press ', b('Enter'), ' at the end of the last line; press ', b('Enter'), ' twice on an empty line to step out below the panel.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Use them sparingly.'), ' One or two on a page stand out. When every other paragraph is in a box, nothing stands out, and readers learn to skip them.')),
      li(p(b('Lead with the point.'), ' Start with a few words in bold that say what the panel is about, as the examples above do, so someone skimming gets it without reading on.')),
      li(p(b('Keep one idea to a panel.'), ' Two warnings in one box are easy to half-read; two boxes are not.')),
      li(p(b('Do not stack them.'), ' Several panels in a row read as a wall of color. Move the least important into the text.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(b('Expand'), ' hides detail most readers can skip behind a title they click to open.')),
      li(p(b('Decision'), ' records a decision the team made, so it can be found later.')),
      li(p(b('Blockquote'), ' sets apart a quotation, rather than something the reader must act on.')),
    ),
  ))

  // ================================================== Creating a space (manual)
  const spaces = await ensure('Spaces', manual)
  const creating = await ensure('Creating a space', spaces)
  await page('Creating a space', spaces, doc(
    p('A ', b('space'), ' is a home for a set of pages that belong together, like a binder on a shelf. Each space has its own page tree, its own home page, and its own list of who can read and edit it. This page helps you decide what deserves a space of its own, then walks you through making one.'),

    h(2, 'What a space is for'),
    p('Give something its own space when it has its own audience: a group of people who read and write it together, and who you might want to give different access from everyone else. Some common ways teams divide things up:'),
    ul(
      li(p(b('By team,'), ' such as Engineering, Marketing or People: each team owns its own processes and notes.')),
      li(p(b('By project,'), ' such as Kestrel launch or Office move: the work has a start and an end, and people from several teams join in.')),
      li(p(b('By audience,'), ' such as Team handbook or Customer help: the same material is read by people who should not see everything else.')),
    ),
    panel('success', p(b('Start with fewer, larger spaces.'), ' It is easier to find things in three well-organized spaces than in twenty small ones, and a page can be moved to another space later with everything under it.')),

    h(2, 'Before you start'),
    p('You need the right to create spaces. Everyone has it by default; if you do not see a ', b('New space'), ' button in step 1, an administrator has turned it off for your role, and they can create the space for you.'),

    step(1, 'Open Spaces and choose New space'),
    p('Choose ', b('Spaces'), ' at the top of any page, then ', b('New space'), ' at the top right.'),
    ...(await picture(creating, 'space-new', 'The Spaces page with the New space button', 'The Spaces page. New space is at the top right.')),

    step(2, 'Give it a key and a name'),
    p('A short form opens above the list.'),
    ...(await picture(creating, 'space-form', 'The new space form, filled in', 'Key and name are required; the description is optional. Create is at the end.')),
    ul(
      li(p(b('Key:'), ' a short code for the space, such as TEAM or ENG2: two to 50 letters and digits, starting with a letter. It becomes part of every page’s address, as in /spaces/TEAM, so it cannot be changed later. Tesria makes it capitals as you type.')),
      li(p(b('Name:'), ' what everyone sees in lists and at the top of the space, such as Team handbook. You can change it later in the space’s settings.')),
      li(p(b('Description:'), ' optional. One line on what the space is for, shown on the Spaces page to help people pick the right one.')),
    ),

    step(3, 'Choose Create'),
    p('It is the button at the end of the form, boxed in the picture above. The space appears in the list. Open it to find an empty home page with a ', b('Create the first one'), ' button.'),

    h(2, 'What a new space starts with'),
    ul(
      li(p(b('Open to everyone signed in.'), ' Anyone with an account can read it, edit it and change its settings.')),
      li(p(b('Not public.'), ' People who are not signed in cannot see it.')),
      li(p(b('An icon made from its key,'), ' which you can change to an emoji or a picture.')),
      li(p(b('Every export allowed:'), ' PDF, Markdown, HTML, a static site and a wiki pack.')),
    ),
    panel('warning', p(b('Want it private?'), ' A new space is open to everyone signed in, which is right for most team spaces. For anything that should be seen by only some people, such as salaries or a confidential project, limit it straight away, before you write anything in it. See ', b('Who can see a space'), '.')),

    h(2, 'Next steps'),
    ul(
      li(p(b('Write the first page.'), ' A good first page says what the space is for and who looks after it. See ', b('Creating a page'), '.')),
      li(p(b('Give it an icon'), ' so people recognize it at a glance. See ', b('Space icons'), '.')),
      li(p(b('Decide who can see it.'), ' See ', b('Who can see a space'), '.')),
    ),
  ))
}

// Pictures these pages no longer use, taken down so they do not linger in the
// page's attachments or in the exported pack.
const RETIRED = {
  'Trusting the local certificate': ['trust-top.png', 'trust-steps.png', 'trust-steps-windows.png', 'trust-check.png', 'trust-iphone.phone.png'],
  'Creating a space': ['space-create.png'],
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }
  // Opening Tesria by name goes straight after Trusting the local
  // certificate, which it follows on from; a new page lands at the end.
  const install = find(tree, 'Installation and operations')
  if (install) {
    const kids = install.children ?? []
    const trustAt = kids.findIndex((n) => n.title === 'Trusting the local certificate')
    const byName = kids.findIndex((n) => n.title === 'Opening Tesria by name')
    if (trustAt >= 0 && byName >= 0 && byName !== trustAt + 1) {
      const index = byName > trustAt ? trustAt + 1 : trustAt
      await author.call('PUT', `/api/pages/${kids[byName].id}/move`, { parentPageId: install.id, index })
      console.log('  moved Opening Tesria by name after Trusting the local certificate')
    }
  }

  // Templates goes after Creating a page, where the old text mentioned them.
  const manual = find(tree, 'User manual')
  const pagesSection = manual && (manual.children ?? []).find((n) => n.title === 'Pages')
  if (pagesSection) {
    const kids = pagesSection.children ?? []
    const after = kids.findIndex((n) => n.title === 'Creating a page')
    const at = kids.findIndex((n) => n.title === 'Templates')
    if (after >= 0 && at >= 0 && at !== after + 1) {
      await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: pagesSection.id, index: at > after ? after + 1 : after })
      console.log('  moved Templates after Creating a page')
    }
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
