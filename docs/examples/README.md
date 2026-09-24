# API examples

The two scripts the Support site's "Code examples" page shows
(`scripts/support/sections/developers.mjs` reads them from here), so the
page and the code cannot drift apart. They do the same five things: list
spaces, search one, write a page, change it without overwriting anyone,
and label it.

```bash
export TESRIA_URL=https://wiki.example.com
export TESRIA_TOKEN=cct_...
export TESRIA_SPACE=TEAM
node tesria.mjs          # Node 18 or later
python3 tesria.py        # Python 3.9 or later, standard library only
```

Both were run against Tesria 0.6.0-dev on 2026-09-24. They write pages: point
them at a space you can throw away.
