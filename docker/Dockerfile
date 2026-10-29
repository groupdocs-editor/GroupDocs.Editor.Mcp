FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/GroupDocs.Editor.Mcp/GroupDocs.Editor.Mcp.csproj \
    -c Release -o /app --no-self-contained

FROM base AS final

# Fonts only. GroupDocs.Editor draws through Aspose.Drawing (managed) and SkiaSharp, whose Linux
# natives ship inside the NuGet package — so no libgdiplus and no System.Drawing flag. Saving
# edited content to PDF or XPS lays text out and needs real fonts:
#   libfontconfig1            — font discovery
#   fontconfig                — the fc-cache tool; sibling images get it through libgdiplus,
#                               which this one does not install
#   ttf-mscorefonts-installer — Arial / Times New Roman and the metric-compatible set Office
#                               documents reference; EULA pre-accepted via debconf
# fc-cache rebuilds the font index. Mirrors the Linux step in the Tests repo's integration.yml.
USER root
RUN apt-get update \
 && echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections \
 && apt-get install -y --no-install-recommends libfontconfig1 fontconfig ttf-mscorefonts-installer \
 && fc-cache -f -v \
 && rm -rf /var/lib/apt/lists/*

# OCI image labels. VERSION is supplied by the publish workflow via
# --build-arg VERSION=YY.MM.N. Default 'dev' makes local `docker build` runs
# self-identify instead of inheriting the base image's misleading version.
ARG VERSION=dev
LABEL org.opencontainers.image.title="GroupDocs.Editor.Mcp" \
      org.opencontainers.image.description="MCP server exposing GroupDocs.Editor for .NET as AI-callable tools (GetDocumentInfo, ExtractHtml, SaveFromHtml, ReplaceText, GetSupportedFormats, GetFormFields, FillFormFields) — edit Word, Excel, PowerPoint and PDF documents without losing formatting, for Claude, Cursor, GitHub Copilot, and other MCP agents." \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.source="https://github.com/groupdocs-editor/GroupDocs.Editor.Mcp" \
      org.opencontainers.image.url="https://www.nuget.org/packages/GroupDocs.Editor.Mcp" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Aspose Pty Ltd"

COPY --from=build /app .

ENV GROUPDOCS_MCP_STORAGE_PATH=/data
VOLUME /data

# Non-root. On a Linux host, run the container as your own user so it can write into a bind
# mount you own: docker run --user "$(id -u):$(id -g)" ...
USER $APP_UID

ENTRYPOINT ["dotnet", "GroupDocs.Editor.Mcp.dll"]
