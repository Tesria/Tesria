# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Tesria production image (multi-stage)
#   1) build the React SPA
#   2) publish the ASP.NET Core API, embedding the SPA in wwwroot
#   3) slim runtime image serving both API and SPA on port 8080
#
# Build context is the repository ROOT:
#   docker build -f deploy/Dockerfile -t tesria .
# (docker-compose.yml sets this up automatically.)
# ---------------------------------------------------------------------------

# --- Stage 1: web -----------------------------------------------------------
FROM node:22-slim AS web
WORKDIR /web
# Install deps first for better layer caching.
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web/ ./
RUN npm run build          # -> /web/dist

# --- Stage 2: api build -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Publish for the one platform this image runs on (dev-plan 13.1). Without a
# runtime identifier, every native library the image-processing packages
# ship is copied in for every platform they support: Windows builds with
# their debug symbols, macOS, musl, loongarch. That was over 500 MB of files
# a Linux container never loads, and it grew by another 150 MB when the SVG
# renderer arrived. BuildKit sets TARGETARCH (amd64 or arm64).
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /tmp/rid
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj -r "$(cat /tmp/rid)"
COPY src/Api/ src/Api/
# Embed the built SPA so `dotnet publish` includes it in the image.
RUN rm -rf src/Api/wwwroot && mkdir -p src/Api/wwwroot
COPY --from=web /web/dist/ src/Api/wwwroot/
# The release version (dev-plan 16.1): GitHub Actions passes the tag's
# number, such as 0.5.0. Without it the build keeps Api.csproj's
# "<next release>-dev", so a local image can never pass for a release.
ARG TESRIA_VERSION=
RUN dotnet publish src/Api/Api.csproj -c Release -o /app -r "$(cat /tmp/rid)" --self-contained false /p:UseAppHost=false \
    ${TESRIA_VERSION:+/p:InformationalVersion=$TESRIA_VERSION}

# --- Stage 3: runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl is used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app ./

# Attachments are written here; docker-compose mounts a named volume at this path.
RUN mkdir -p /app/uploads && chown -R app:app /app/uploads
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "Tesria.Api.dll"]
