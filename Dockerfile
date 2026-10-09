# Hentarr: Sonarr fork with AniList as the metadata source.
# Builds the frontend and backend from this checkout and runs the app framework-dependent on the ASP.NET runtime.
#
#   docker build -t hentarr .
#   docker run -d --name hentarr -p 8990:8990 -v /path/to/config:/config -v /path/to/media:/media hentarr
#
# Settings are read from environment variables (SONARR__SERVER__PORT, HENTARR_ADULT_FILTER, ...) or /config/config.xml.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=24.19.0

# ---------- frontend ----------
FROM node:${NODE_VERSION}-bookworm-slim AS ui
WORKDIR /src
COPY package.json yarn.lock ./
RUN corepack enable && yarn install --frozen-lockfile --network-timeout 600000
COPY index.html tsconfig.json vite.config.ts ./
COPY frontend ./frontend
RUN yarn build

# ---------- backend ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS backend
ARG TARGETARCH
WORKDIR /src
COPY global.json ./
COPY src ./src
# TARGETARCH is amd64 or arm64 (docker buildx); map it to a .NET runtime identifier.
# Sonarr.Mono is loaded by name at runtime on Linux (see Bootstrap.ASSEMBLIES), so it is published into the same folder.
RUN case "${TARGETARCH:-amd64}" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH ${TARGETARCH}" && exit 1 ;; \
    esac && \
    for project in src/NzbDrone.Console/Sonarr.Console.csproj src/NzbDrone.Mono/Sonarr.Mono.csproj; do \
      dotnet publish "${project}" \
        -c Release -f net10.0 -r "${RID}" --self-contained false \
        -p:Platform=Posix -p:SolutionDir=/src/src/ \
        -o /app/bin -nologo || exit 1; \
    done

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}
LABEL org.opencontainers.image.title="Hentarr" \
      org.opencontainers.image.description="Sonarr fork using AniList (adult anime) as metadata source" \
      org.opencontainers.image.licenses="GPL-3.0"

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl ca-certificates tzdata && \
    rm -rf /var/lib/apt/lists/*

COPY --from=backend /app/bin /app/bin
COPY --from=ui /src/_output/UI /app/bin/UI

# Tells the app it was packaged for Docker, so the built-in updater stays off even if the fork switch is flipped.
RUN printf 'PackageVersion=docker\nPackageAuthor=Hentarr\nUpdateMethod=Docker\nBranch=v5-develop\n' > /app/package_info && \
    mkdir -p /config /media

# A different port and instance name than stock Sonarr so both can run side by side.
# Upstream only accepts instance names that start or end with "Sonarr" (ConfigFileProvider.InstanceName).
ENV SONARR__SERVER__PORT=8990 \
    SONARR__APP__INSTANCENAME="Sonarr - Hentarr" \
    SONARR__UPDATE__MECHANISM=Docker \
    SONARR__LOG__ANALYTICSENABLED=false \
    HENTARR_ADULT_FILTER=adult \
    DOTNET_gcServer=0

VOLUME ["/config", "/media"]
EXPOSE 8990

HEALTHCHECK --interval=60s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -fs "http://localhost:${SONARR__SERVER__PORT}/ping" || exit 1

WORKDIR /app/bin
ENTRYPOINT ["/app/bin/Sonarr", "-nobrowser", "-data=/config"]
