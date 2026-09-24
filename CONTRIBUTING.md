# Contributing to Tesria

Thank you for wanting to help. The full guide, with every step explained,
is in the docs (tesria.com/docs) under **Developers → Contributing**. This file is
the short version.

## Before you start

- Read the [code of conduct](CODE_OF_CONDUCT.md). It applies everywhere the
  project is.
- Found a security problem? Do not open an issue. See [SECURITY.md](SECURITY.md).
- For anything more than a small fix, open an issue first and say what you
  have in mind.

## Build and run it

You need Docker with Compose v2 and Git; for the tests, the .NET 10 SDK and
Node.js 22.

```bash
git clone https://github.com/Tesria/Tesria.git
cd Tesria
cp .env.example .env              # then fill in the values it asks for
docker compose up -d --build      # everything; then open https://localhost
docker compose up -d --build app  # after changing the API or the web app
```

## Test it

```bash
dotnet test tests/Api.Tests                                      # the server, no database needed
cd src/web && npm ci && npm run build && npm run lint && npm test  # the web app
scripts/audit.sh                                                 # dependencies
```

GitHub runs the first two on every push and pull request. Tests prove the
logic; for a change you can see, also try it in a browser.

## Conventions

- One folder per feature under `src/Api/Features`; database changes are EF
  Core migrations (`dotnet ef migrations add <Name> --output-dir
  Infrastructure/Migrations`, from `src/Api`).
- Editor blocks are defined once, in `src/web/src/editor/extensions.ts`.
- Something the caller may not see answers `404`, never `403`.
- After changing a dependency (a package list or a base image), run
  `node scripts/deps/build-manifest.mjs` and commit what it writes: the
  About tab in Administration lists it, and CI checks it is current.
- Every change gets an entry in `docs/CHANGELOG.md`, and
  `docs/architecture.md` is updated when how something works changes.
- Words people read are in US English, plain, without em dashes.

## Propose it

Fork, branch, keep the pull request to one change, and say what it changes,
why, and how you checked it. A maintainer reviews it. Contributions are
under the project's license, the [Apache License 2.0](LICENSE).

## Releases

See **Developers → Contributing → Making a release** in the docs,
and `.github/workflows/release.yml`.
