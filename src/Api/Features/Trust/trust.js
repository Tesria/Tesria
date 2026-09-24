// "Trust this device" (dev-plan 15.5). The page works without this file:
// every device's steps are shown. With it, the page shows only yours, and
// writes the address into the downloads and the final check.
(function () {
  'use strict';
  var d = document;
  // The same rule the server applies before it writes an address into a
  // script (TrustEndpoints.Address); the server checks again regardless.
  var ADDRESS = /^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$/;
  var IPV4 = /^\d{1,3}(\.\d{1,3}){3}$/;

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

  function clean(value) {
    return value.trim().toLowerCase().replace(/^[a-z]+:\/\//, '').replace(/[/:].*$/, '');
  }

  function useAddress() {
    var input = d.getElementById('trust-address');
    var address = clean(input.value);
    var ok = ADDRESS.test(address);
    d.getElementById('trust-address-error').hidden = ok || address === '';
    d.getElementById('trust-ip').hidden = !(ok && IPV4.test(address));
    d.querySelectorAll('[data-download]').forEach(function (a) {
      var kind = a.getAttribute('data-download');
      if (ok) {
        a.href = '/trust/trust-tesria.' + kind + '?address=' + encodeURIComponent(address);
        a.removeAttribute('aria-disabled');
      } else {
        a.removeAttribute('href');
        a.setAttribute('aria-disabled', 'true');
      }
    });
    // Commands with the address in them (the Windows line), rewritten as
    // the address changes; the placeholder stands in until it is valid.
    d.querySelectorAll('[data-template]').forEach(function (el) {
      el.textContent = el.getAttribute('data-template').replace('{address}', ok ? address : 'your-server');
    });
    var open = d.getElementById('trust-open');
    if (open) open.href = ok ? 'https://' + address + '/' : '#';
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
    var input = d.getElementById('trust-address');
    if (input) {
      input.addEventListener('input', useAddress);
      useAddress();
    }
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
