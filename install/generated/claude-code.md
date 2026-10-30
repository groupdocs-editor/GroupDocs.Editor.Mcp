# Claude Code

```bash
claude mcp add groupdocs-editor -- dnx GroupDocs.Editor.Mcp --yes
```

With storage folder and license:

```bash
claude mcp add groupdocs-editor -e GROUPDOCS_MCP_STORAGE_PATH=/path/to/documents -e GROUPDOCS_LICENSE_PATH=/path/to/GroupDocs.Total.lic -- dnx GroupDocs.Editor.Mcp --yes
```

Pin a version by replacing `GroupDocs.Editor.Mcp` with `GroupDocs.Editor.Mcp@26.10.0`.
