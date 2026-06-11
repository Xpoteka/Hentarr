# Stage 1: Build the frontend (architecture independent, always runs on the build host)
FROM --platform=$BUILDPLATFORM node:20-bookworm AS ui
WORKDIR /build

COPY package.json yarn.lock .yarnrc tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000

COPY frontend ./frontend
RUN yarn build --env production

# Stage 2: Build the backend (cross-compiles for the target architecture)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS backend
ARG TARGETARCH
ARG VERSION=
ARG BRANCH=
WORKDIR /build

COPY .editorconfig ./
COPY src ./src

RUN case "$TARGETARCH" in \
        amd64) RID=linux-x64 ;; \
        arm64) RID=linux-arm64 ;; \
        arm)   RID=linux-arm ;; \
        *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    if [ -n "$VERSION" ]; then \
        sed -i "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$VERSION<\/AssemblyVersion>/g" src/Directory.Build.props; \
    fi && \
    if [ -n "$BRANCH" ]; then \
        SAFE_BRANCH=$(echo "$BRANCH" | tr '/' '-') && \
        sed -i "s/<AssemblyConfiguration>[\$()A-Za-z-]\+<\/AssemblyConfiguration>/<AssemblyConfiguration>$SAFE_BRANCH<\/AssemblyConfiguration>/g" src/Directory.Build.props; \
    fi && \
    dotnet msbuild -restore src/Whisparr.sln \
        -p:SelfContained=true \
        -p:Configuration=Release \
        -p:Platform=Posix \
        -p:RuntimeIdentifiers=$RID \
        -p:EnableWindowsTargeting=true \
        -p:EnableAnalyzers=false \
        -t:PublishAllRids && \
    mv "_output/net6.0/$RID/publish" /app-bin && \
    rm -f /app-bin/ServiceInstall.* /app-bin/ServiceUninstall.* /app-bin/Whisparr.Windows.* && \
    chmod a+x /app-bin/Whisparr && \
    find /app-bin -name ffprobe -exec chmod a+x {} \;

# Stage 3: Runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:6.0 AS runtime
ARG VERSION=dev
ARG BRANCH=docker

ENV XDG_CONFIG_HOME=/config

COPY --from=backend /app-bin /app/bin
COPY --from=ui /build/_output/UI /app/bin/UI

# Mark this install as docker-managed so the built-in updater is disabled
RUN printf 'PackageAuthor=Hentarr\nUpdateMethod=Docker\nUpdateMethodMessage=Update the Docker image to receive updates\nBranch=%s\nPackageVersion=%s\n' \
        "$BRANCH" "$VERSION" > /app/package_info && \
    mkdir -p /config

EXPOSE 6969
VOLUME /config

ENTRYPOINT ["/app/bin/Whisparr", "-nobrowser", "-data=/config"]
