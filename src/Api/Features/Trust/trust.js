// "Trust this device" (dev-plan 15.5). The page works without this file:
// every device's steps are shown. With it, the page shows only yours, and
// writes the address into the commands and the final check. Checking the
// fingerprint is optional (2026-09-27, the owner's decision, SSH-style): a
// pasted one swaps each command for its checked form. It never supplies a
// fingerprint itself: the person pastes the one they got from the server.
(function () {
  'use strict';
  var d = document;
  // The same rule the server applies before it writes an address into a
  // script (TrustEndpoints.Address); the server checks again regardless.
  var ADDRESS = /^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$/;
  var IPV4 = /^\d{1,3}(\.\d{1,3}){3}$/;
  var PLACEHOLDER = 'PASTE-THE-FINGERPRINT';

  function guessDevice() {
    var ua = navigator.userAgent || '';
    // iPadOS reports itself as a Mac; a Mac has no touch screen.
    if (/iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'ios';
    if (/Android/.test(ua)) return 'android';
    if (/Windows/.test(ua)) return 'windows';
    if (/Macintosh|Mac OS X/.test(ua)) return 'mac';
    if (/Linux|X11|CrOS/.test(ua)) return 'linux';
    return 'windows';
  }

  function choose(device) {
    d.querySelectorAll('.trust-device').forEach(function (b) {
      var on = b.getAttribute('data-device') === device;
      b.classList.toggle('is-active', on);
      b.setAttribute('aria-checked', on ? 'true' : 'false');
    });
    d.querySelectorAll('.trust-guide').forEach(function (g) {
      g.hidden = g.getAttribute('data-for') !== device;
    });
    // Firefox on an iPhone is Safari underneath and uses the system's list;
    // everywhere else it keeps its own.
    var firefox = d.querySelector('.trust-firefox');
    if (firefox) firefox.hidden = device === 'ios';
  }

  // What was typed, as a name and a port (WIN-002): a Tesria on ports of
  // its own is opened at, say, localhost:8443, and its certificate is
  // downloaded from its plain-HTTP port, which the server gives on the
  // input. A port that is the plain-HTTP one (someone typed this page's own
  // address) means the HTTPS one. null when it is not an address.
  function parse(value, input) {
    var text = value.trim().toLowerCase().replace(/^[a-z]+:\/\//, '').replace(/[/?#].*$/, '');
    var m = /^([^:]*)(?::(\d{1,5}))?$/.exec(text);
    if (!m || !ADDRESS.test(m[1])) return null;
    var httpPort = Number(input.getAttribute('data-http-port')) || 80;
    var httpsPort = Number(input.getAttribute('data-https-port')) || 443;
    var port = m[2] === undefined ? 443 : Number(m[2]);
    if (port < 1 || port > 65535) return null;
    if (port === httpPort && port !== httpsPort) port = httpsPort;
    return {
      host: m[1],
      address: m[1] + (port === 443 ? '' : ':' + port),
      http: m[1] + (httpPort === 80 ? '' : ':' + httpPort),
    };
  }

  // 64 hex digits, whatever separators and case it was pasted with.
  function fingerprint() {
    var input = d.getElementById('trust-fingerprint');
    var raw = input ? input.value : '';
    var hex = raw.replace(/[^0-9a-fA-F]/g, '').toUpperCase();
    var ok = hex.length === 64;
    var error = d.getElementById('trust-fingerprint-error');
    if (error) error.hidden = ok || raw.trim() === '';
    var good = d.getElementById('trust-fingerprint-ok');
    if (good) good.hidden = !ok;
    return ok ? { hex: hex, pairs: hex.match(/../g).join(':') } : null;
  }

  function update() {
    var input = d.getElementById('trust-address');
    var typed = input.value.trim();
    var where = parse(typed, input);
    d.getElementById('trust-address-error').hidden = !!where || typed === '';
    d.getElementById('trust-ip').hidden = !(where && IPV4.test(where.host));
    var http = (where || parse('your-server', input)).http;
    var fp = fingerprint();
    d.querySelectorAll('[data-template]').forEach(function (el) {
      var checked = fp && el.getAttribute('data-template-checked');
      el.textContent = (checked || el.getAttribute('data-template'))
        .split('{http}').join(http)
        .split('{fingerprint}').join(fp ? fp.pairs : PLACEHOLDER)
        .split('{hex}').join(fp ? fp.hex : PLACEHOLDER);
    });
    var open = d.getElementById('trust-open');
    if (open) open.href = where ? 'https://' + where.address + '/' : '#';
  }

  // navigator.clipboard needs a secure page, and this one is usually plain
  // HTTP, so this falls back to the older way of copying a selection.
  function copy(text, button) {
    function done() {
      var was = button.textContent;
      button.textContent = 'Copied';
      setTimeout(function () { button.textContent = was; }, 1500);
    }
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(done, function () {});
      return;
    }
    var area = d.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    d.body.appendChild(area);
    area.select();
    try { if (d.execCommand('copy')) done(); } catch (e) { /* the text is still there to select by hand */ }
    d.body.removeChild(area);
  }

  function wire() {
    d.querySelectorAll('.trust-device').forEach(function (b) {
      b.addEventListener('click', function () { choose(b.getAttribute('data-device')); });
    });
    choose(guessDevice());
    ['trust-address', 'trust-fingerprint'].forEach(function (id) {
      var input = d.getElementById(id);
      if (input) input.addEventListener('input', update);
    });
    if (d.getElementById('trust-address')) update();
    d.querySelectorAll('[data-copy]').forEach(function (b) {
      b.addEventListener('click', function () {
        var code = b.parentNode.querySelector('code');
        if (code) copy(code.textContent, b);
      });
    });
  }

  if (d.readyState === 'loading') d.addEventListener('DOMContentLoaded', wire);
  else wire();
})();
