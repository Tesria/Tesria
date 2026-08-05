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
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj
COPY src/Api/ src/Api/
# Embed the built SPA so `dotnet publish` includes it in the image.
RUN rm -rf src/Api/wwwroot && mkdir -p src/Api/wwwroot
COPY --from=web /web/dist/ src/Api/wwwroot/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app /p:UseAppHost=false

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
