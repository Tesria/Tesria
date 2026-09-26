# PDF rendering sidecar. Playwright's own image ships a Chromium matched to
# the library version, which is the whole reason to use it.
#
# The image tag and `playwright-core` in package.json must name the SAME
# version, and that version is pinned exactly (no caret): a range resolves
# forward on the next build, the library then looks for a Chromium the image
# does not have, and every PDF fails with "Executable doesn't exist". Bump
# both together or neither.
# Build context is the repository ROOT (see docker-compose.yml).
FROM mcr.microsoft.com/playwright:v1.63.0-noble

WORKDIR /app
ENV NODE_ENV=production

COPY pdf/package.json pdf/package-lock.json* ./
# `npm ci` when a lockfile exists, `npm install` on first build.
RUN if [ -f package-lock.json ]; then npm ci --omit=dev; else npm install --omit=dev; fi

COPY pdf/server.js ./
# Tesria's license and notices, and every bundled package's license text
# (the review's LIC-01): this image is published on its own, so it carries
# them itself; some packages, Hocuspocus among them, ship no license file.
COPY LICENSE NOTICE src/Api/About/THIRD-PARTY-NOTICES.txt /usr/share/doc/tesria/

EXPOSE 8091
USER pwuser
CMD ["node", "server.js"]
