# Tesria's docs

`support-pack.zip` is Tesria's docs, published at tesria.com/docs: the Docs
space (called Support until 2026-09-24), exported as a wiki pack (dev-plan
10.5). The file keeps its old name so existing links to it still work. The wiki is the source of truth. This copy is here
so the site survives anything that happens to an instance, and so anyone can
run it locally.

**To read it in your own instance:** Spaces → **Import a pack**, and choose
this file. The imported space is private to you until you give others
access.

**To regenerate it** after the pages change, from the repository root:

```bash
scripts/support/export-support.sh
```

That writes this file again and also exports the static site for
tesria.com, checked against Cloudflare's limits. The pages themselves are
written by `scripts/support/publish-support.sh`.
