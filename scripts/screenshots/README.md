# Screenshot harness

Takes screenshots of the running instance, for documentation written *in*
Tesria (the `MANUAL` space) or anywhere else a picture of the real UI is
wanted. Built for the user manual on 2026-09-11.

```sh
export SHOT_EMAIL=someone@example.com SHOT_PASSWORD='…'
scripts/screenshots/run.sh path/to/spec.json          # every shot
scripts/screenshots/run.sh path/to/spec.json login    # just one
```

PNGs land in `shots/` beside the spec. `manual-space.example.json` is the
spec that produced the manual's screenshots: page ids in it are from this
instance, so treat it as a worked example rather than something to re-run
unchanged.

## A shot

```json
{
  "name": "insert-menu",
  "url": "/spaces/MANUAL/pages/<id>/edit",
  "viewport": { "width": 1440, "height": 1400 },
  "settle": 4500,
  "steps": [{ "click": "[title='Insert an element']" }, { "wait": 900 }],
  "clipTo": ".toolbar-dropdown__menu",
  "clipPad": 14,
  "annotate": [{ "type": "circle", "target": ".btn--primary", "pad": 5 }]
}
```

| Field | Does |
|---|---|
| `url` | Where to go. Omit to keep the current page. |
| `anon` | Use a second, signed-out context: for what a visitor sees. |
| `steps` | `click`, `hover`, `type`+`selector`, `press`, `keys` (literal typing), `eval`, `wait`, `waitFor`. |
| `clipTo` | Crop to an element, or to the union of several. |
| `clipPad` / `clipTrim` | Breathing room; pixels to shave off the bottom. |
| `clip` | An explicit rectangle instead. |
| `fullPage` | The whole scroll height. |
| `annotate` | `circle`, `box`, `arrow` (with `from`: left/right/above/below), `note`. |

Annotations are drawn as a **DOM overlay before the capture**, positioned
from `getBoundingClientRect()`, not painted onto the PNG afterwards. They
come out as crisp as the UI beneath them, and they are described per shot
rather than pushed around in pixels. An arrow needs room *outside* the
element it points at, so leave enough `clipPad` for it or it will be cropped
off.

## Why it runs where it runs

Chromium comes from the **PDF sidecar's image** (`tesria-pdf`), which already
carries one matched to its Playwright version. Nothing to install, and no
second copy to keep in step.

It runs inside **Caddy's network namespace** (`--network
container:tesria-caddy-1`), so `https://tesria.localhost` is this instance
through the real proxy. Going straight to the app container instead fails
twice over, both times silently:

- the session cookie is `Secure`, so plain HTTP drops it and every shot comes
  out signed-out;
- `/collab` is routed by Caddy, so the collaborative editor never loads a
  page's content and every editor shot is an empty document under a toolbar.

Chromium also force-upgrades a *named* host from `http` to `https` and will
not be talked out of it by `--disable-features=HttpsUpgrades`; only an IP
literal or a real HTTPS endpoint gets past that. Hence: real HTTPS, through
Caddy, with `ignoreHTTPSErrors` for the internal CA.
