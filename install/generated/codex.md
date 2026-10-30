# Codex CLI (OpenAI)

```bash
codex mcp add groupdocs-editor -- dnx GroupDocs.Editor.Mcp --yes
```

Or add to `~/.codex/config.toml`:

```toml
[mcp_servers.groupdocs-editor]
command = "dnx"
args = ["GroupDocs.Editor.Mcp", "--yes"]

[mcp_servers.groupdocs-editor.env]
GROUPDOCS_MCP_STORAGE_PATH = "/path/to/documents"
GROUPDOCS_MCP_OUTPUT_PATH = "/path/to/documents"
GROUPDOCS_LICENSE_PATH = ""   # empty = evaluation mode; set to your GroupDocs.Total.lic to lift limits
```

Pin a version by replacing `GroupDocs.Editor.Mcp` with `GroupDocs.Editor.Mcp@26.10.0`.
