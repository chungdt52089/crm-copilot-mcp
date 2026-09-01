# syntax=docker/dockerfile:1
#
# Build context MUST be the repo root — central package management lives in the
# root Directory.Packages.props and Contracts is a sibling project. See
# compose.yaml's `build.context: .`.
#
# SDK tag is pinned, not floating: global.json requires 10.0.400 with
# rollForward=latestPatch, so an SDK outside the 10.0.4xx band fails restore.
#
# The Razor page and wwwroot assets are carried by the Web SDK's own publish
# rules. Since P0-12, Web also reads data/auth/users.json (the synthetic login
# accounts), so that folder must be in the build context like the other two
# services' data/ folders — UserStore fails fast at startup without it.

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# Restore layer — only the files that affect NuGet resolution, so editing
# application source does not invalidate the restore cache.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CrmCopilot.Contracts/CrmCopilot.Contracts.csproj src/CrmCopilot.Contracts/
COPY src/CrmCopilot.Web/CrmCopilot.Web.csproj             src/CrmCopilot.Web/
RUN dotnet restore src/CrmCopilot.Web/CrmCopilot.Web.csproj

# Build layer — only this service's own sources plus Contracts.
COPY src/CrmCopilot.Contracts/ src/CrmCopilot.Contracts/
COPY src/CrmCopilot.Web/       src/CrmCopilot.Web/
COPY data/auth/                data/auth/
RUN dotnet publish src/CrmCopilot.Web/CrmCopilot.Web.csproj \
      -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The base image already sets ASPNETCORE_HTTP_PORTS=8080 and defines
# APP_UID=1654, so no extra ENV and no root process. No package is installed:
# compose's healthcheck uses bash /dev/tcp, which this image already has.
# GEMINI_API_KEY is deliberately NOT baked in — compose injects it at runtime.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CrmCopilot.Web.dll"]
