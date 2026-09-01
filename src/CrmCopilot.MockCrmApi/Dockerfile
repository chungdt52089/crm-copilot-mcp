# syntax=docker/dockerfile:1
#
# Build context MUST be the repo root — CrmCopilot.MockCrmApi.csproj globs
# ..\..\data\crm\*.json and central package management lives in the root
# Directory.Packages.props. See compose.yaml's `build.context: .`.
#
# SDK tag is pinned, not floating: global.json requires 10.0.400 with
# rollForward=latestPatch, so an SDK outside the 10.0.4xx band fails restore.

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# Restore layer — only the files that affect NuGet resolution, so editing
# application source does not invalidate the restore cache.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CrmCopilot.Contracts/CrmCopilot.Contracts.csproj   src/CrmCopilot.Contracts/
COPY src/CrmCopilot.MockCrmApi/CrmCopilot.MockCrmApi.csproj src/CrmCopilot.MockCrmApi/
RUN dotnet restore src/CrmCopilot.MockCrmApi/CrmCopilot.MockCrmApi.csproj

# Build layer — only this service's own sources plus Contracts, so a change in
# McpServer/Web cannot bust this image's cache. data/crm must be present before
# publish: the csproj copies it to the output as data/crm/, which is where
# CrmDatasetLoader.LoadFromAppBaseDirectory() reads it from at runtime.
COPY src/CrmCopilot.Contracts/   src/CrmCopilot.Contracts/
COPY src/CrmCopilot.MockCrmApi/  src/CrmCopilot.MockCrmApi/
COPY data/crm/                   data/crm/
RUN dotnet publish src/CrmCopilot.MockCrmApi/CrmCopilot.MockCrmApi.csproj \
      -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The base image already sets ASPNETCORE_HTTP_PORTS=8080 and defines
# APP_UID=1654, so no extra ENV and no root process. No package is installed:
# compose's healthcheck uses bash /dev/tcp, which this image already has.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CrmCopilot.MockCrmApi.dll"]
