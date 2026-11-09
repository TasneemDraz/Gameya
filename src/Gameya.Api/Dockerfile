# Build context is the repository root:  docker build -f src/Gameya.Api/Dockerfile .
# The build stage always runs on the build machine's architecture and cross-publishes with `-a $TARGETARCH`,
# so the image can be cross-built for another architecture on any runner without QEMU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first (project files only) so source edits do not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Gameya.Domain/Gameya.Domain.csproj src/Gameya.Domain/
COPY src/Gameya.Application/Gameya.Application.csproj src/Gameya.Application/
COPY src/Gameya.Infrastructure/Gameya.Infrastructure.csproj src/Gameya.Infrastructure/
COPY src/Gameya.Api/Gameya.Api.csproj src/Gameya.Api/
RUN dotnet restore src/Gameya.Api/Gameya.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/Gameya.Api/Gameya.Api.csproj -c Release -a $TARGETARCH --no-restore -o /app \
    && mkdir -p /data/files

# Chiseled "extra" image: ships tzdata (Africa/Cairo) and ICU, but no shell or package manager,
# so OS-package CVEs such as apt's never reach the image. It already runs as the non-root $APP_UID.
# No RUN steps here (there is no shell anyway), which keeps a cross-architecture build emulation-free.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app .
# Pre-created and owned by the app user: a fresh named volume mounted at /data/files inherits this ownership.
COPY --from=build --chown=$APP_UID:$APP_UID /data /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gameya.Api.dll"]
