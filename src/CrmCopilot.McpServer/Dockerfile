# syntax=docker/dockerfile:1
#
# Build context MUST be the repo root — CrmCopilot.McpServer.csproj globs
# ..\..\data\knowledge\*.json and central package management lives in the root
# Directory.Packages.props. See compose.yaml's `build.context: .`.
#
# SDK tag is pinned, not floating: global.json requires 10.0.400 with
# rollForward=latestPatch, so an SDK outside the 10.0.4xx band fails restore.
#
# This same image backs two compose services: the long-running `mcpserver` web
# host, and the profile-gated one-shot `ingest` job, which overrides the
# entrypoint with `--ingest-knowledge`.

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# Restore layer — only the files that affect NuGet resolution, so editing
# application source does not invalidate the restore cache.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CrmCopilot.Contracts/CrmCopilot.Contracts.csproj src/CrmCopilot.Contracts/
COPY src/CrmCopilot.McpServer/CrmCopilot.McpServer.csproj src/CrmCopilot.McpServer/
RUN dotnet restore src/CrmCopilot.McpServer/CrmCopilot.McpServer.csproj

# Build layer — only this service's own sources plus Contracts. data/knowledge
# must be present before publish: the csproj copies it to the output as
# data/knowledge/, which is where KnowledgeSourceLoader.LoadFromAppBaseDirectory()
# reads the 21 ingestion documents from at runtime.
COPY src/CrmCopilot.Contracts/ src/CrmCopilot.Contracts/
COPY src/CrmCopilot.McpServer/ src/CrmCopilot.McpServer/
COPY data/knowledge/           data/knowledge/
RUN dotnet publish src/CrmCopilot.McpServer/CrmCopilot.McpServer.csproj \
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
ENTRYPOINT ["dotnet", "CrmCopilot.McpServer.dll"]
