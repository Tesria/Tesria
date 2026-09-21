# Real-time collaboration sidecar (Hocuspocus/Yjs).
# Build context is the repository ROOT (see docker-compose.yml).
# The editor schema and the reconciliation that depends on it are built from
# src/web, not restated here (dev-plan 8.6). A node declared in extensions.ts
# and missing from the sidecar's schema would be dropped from every document
# the sidecar reconciled, silently, so there is exactly one definition and
# this stage rebuilds it whenever that definition changes.
FROM node:22-slim AS schema
# The repo's own layout, so the config's `../../collab/vendor` output path
# means the same thing here as it does on a developer's machine.
WORKDIR /src/web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web/ ./
RUN npm run build:schema

FROM node:22-slim

WORKDIR /app
ENV NODE_ENV=production

# Install dependencies first for better layer caching. `npm ci` installs
# exactly what the lockfile says and fails if it disagrees with package.json,
# so the image is reproducible and auditable (dev-plan 3.6).
COPY collab/package.json collab/package-lock.json ./
RUN npm ci --omit=dev

COPY collab/server.js ./
COPY --from=schema /collab/vendor/collab-schema.js ./vendor/

EXPOSE 8090
USER node
CMD ["node", "server.js"]
