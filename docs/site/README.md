# Tesria's docs

The Docs space (called Support until 2026-09-24), published at tesria.com/docs,
is written into a Tesria instance by `scripts/docs/publish-docs.sh`. The wiki
is the source of truth. This folder is where its exports land; they are not
committed.

**To export and publish them**, from the repository root:

```bash
scripts/docs/export-docs.sh
scripts/docs/release-docs.sh
```

The first writes `docs-pack.zip` (the space as a wiki pack) and `docs-site.zip`
(the static site for tesria.com/docs) here, checked against Cloudflare's
limits. The second uploads both to the standing **docs** release on GitHub,
replacing the last upload; `scripts/docs/release-docs.sh v0.6.0` attaches them
to a version's release instead, once that release is published.

**To read the docs in your own instance:** download `docs-pack.zip` from the
repository's releases, then in Tesria choose Spaces, then **Import a pack**,
and choose the file. The imported space is private to you until you give
others access.
