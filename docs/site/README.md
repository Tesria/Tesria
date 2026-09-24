# Tesria's docs

`docs-pack.zip` is Tesria's docs, published at tesria.com/docs: the Docs
space (called Support until 2026-09-24), exported as a wiki pack (dev-plan
10.5). The wiki is the source of truth. This copy is here so the docs
survive anything that happens to an instance, and so anyone can read them
locally.

**To read it in your own instance:** Spaces → **Import a pack**, and choose
this file. The imported space is private to you until you give others
access.

**To regenerate it** after the pages change, from the repository root:

```bash
scripts/docs/export-docs.sh
```

That writes this file again and also exports the static site for
tesria.com/docs (`docs-site.zip`, here, never committed), checked against
Cloudflare's limits. The pages themselves are
written by `scripts/docs/publish-docs.sh`.
