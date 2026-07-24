# Real-time collaboration sidecar (Hocuspocus/Yjs).
# Build context is the repository ROOT (see docker-compose.yml).
FROM node:22-slim

WORKDIR /app
ENV NODE_ENV=production

# Install dependencies first for better layer caching.
COPY collab/package.json collab/package-lock.json* ./
RUN npm install --omit=dev

COPY collab/server.js ./

EXPOSE 8090
USER node
CMD ["node", "server.js"]
