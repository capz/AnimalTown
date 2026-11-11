# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk@sha256:1b955f179b02df4f0eb5fadedaa898cbe44d620a64aab52eec72c8e91d1355b5 AS dotnet8
FROM mcr.microsoft.com/dotnet/sdk@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS dotnet10
FROM mcr.microsoft.com/powershell@sha256:810c4f1e0c9d23022c3ec18c50a6205ee4b60766f1739d329b2948df1fd7d5b0 AS powershell
FROM ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85

LABEL org.opencontainers.image.source="https://github.com/capz/AnimalTown"
LABEL org.opencontainers.image.description="AnimalTown Linux GameCube and backend build toolchain"
RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu72 libgssapi-krb5-2 libssl3 zlib1g libfontconfig1 libfreetype6 libharfbuzz0b \
    ca-certificates tzdata && rm -rf /var/lib/apt/lists/*
# Merge side-by-side SDK/runtime directories; keep the newer dotnet host last.
COPY --from=dotnet8 /usr/share/dotnet/ /usr/share/dotnet/
COPY --from=dotnet10 /usr/share/dotnet/ /usr/share/dotnet/
COPY --from=powershell /opt/microsoft/powershell/7/ /opt/microsoft/powershell/7/
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="/usr/share/dotnet:/opt/microsoft/powershell/7:/opt/devkitpro/devkitPPC/bin:${PATH}"
RUN curl -fsSL https://github.com/cli/cli/releases/download/v2.103.3/gh_2.103.3_linux_amd64.tar.gz -o /tmp/gh.tar.gz \
    && echo "4c806978ffce1cec70853302a56d70ac6da4f19bae7adca4e1a8ab00633dd91d  /tmp/gh.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/gh.tar.gz -C /tmp \
    && install -m 755 /tmp/gh_2.103.3_linux_amd64/bin/gh /usr/local/bin/gh \
    && rm -rf /tmp/gh.tar.gz /tmp/gh_2.103.3_linux_amd64
# Trust only the documented bind-mount locations, not arbitrary host repositories.
RUN git config --system core.autocrlf input \
    && git config --system --add safe.directory /workspace \
    && dotnet --info && dotnet --list-sdks \
    && pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' \
    && powerpc-eabi-gcc --version && gh --version
COPY nkit.ps1 nkit.lock.json NKit.LICENSE /opt/animaltown-nkit/
RUN pwsh -NoProfile -File /opt/animaltown-nkit/nkit.ps1 -CacheRoot /opt/animaltown-nkit/cache
ENV ANIMALTOWN_NKIT_CACHE=/opt/animaltown-nkit/cache
WORKDIR /workspace
CMD ["pwsh", "-NoLogo", "-NoProfile", "-File", "tools/build-linux.ps1"]
