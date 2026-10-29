# AGENTS.md — Guide for AI coding agents

Brief orientation for AI coding agents (Claude Code, Copilot, Cursor, Aider, Amp, Codex) working in this repository.

## What this repo is

`GroupDocs.Editor.Mcp` — an MCP server that exposes [GroupDocs.Editor for .NET](https://products.groupdocs.com/editor/net/)
as AI-callable tools for editing documents without losing their formatting. Shipped as a .NET tool
(`dnx GroupDocs.Editor.Mcp --yes`), as Docker images (`ghcr.io/groupdocs-editor/editor-net-mcp`,
`groupdocs/editor-net-mcp`) and in the MCP Registry (`io.github.groupdocs-editor/groupdocs-editor-mcp`).
Built on the shared `GroupDocs.Mcp.Core` framework (storage, file resolution, licensing, error filter,
`get_license_status`).

Integration tests against the **published** artifact live in the sibling repo
[GroupDocs.Editor.Mcp.Tests](https://github.com/groupdocs-editor/GroupDocs.Editor.Mcp.Tests).

## MCP tools exposed

| Wire name | Class | What it does |
|---|---|---|
| `get_document_info` | `GetDocumentInfoTool` | Family, format, pages/worksheets/slides, size, encryption, save targets (JSON) |
| `extract_html` | `ExtractHtmlTool` | Document or one worksheet/slide/PDF page range → editable HTML in storage + preview |
| `save_from_html` | `SaveFromHtmlTool` | HTML → DOCX/XLSX/PPTX/PDF/TXT/MD/EPUB/…; splices one part back into `sourceFile` |
| `replace_text` | `ReplaceTextTool` | Formatting-preserving find/replace on decoded text nodes; zero matches writes nothing |
| `get_supported_formats` | `GetSupportedFormatsTool` | Load/save catalog per family, or save targets of one file (JSON) |
| `get_form_fields` | `GetFormFieldsTool` | Word form fields with type and value (JSON) |
| `fill_form_fields` | `FillFormFieldsTool` | Sets Word form-field values; reports unknown names and rejected values |
| `get_license_status` | (GroupDocs.Mcp.Core) | Licensing mode, metered consumption, engine version |

## Folder layout

```
src/GroupDocs.Editor.Mcp/
  Program.cs                    ← host: AddGroupDocsMcp().AddLocalStorage("./Files"), stdio transport
  EditorLicenseManager.cs       ← licence file + metered key + consumption + engine marker
  .mcp/server.json              ← MCP Registry manifest (version in lockstep with build/dependencies.props)
  Tools/
    GetDocumentInfoTool.cs  ExtractHtmlTool.cs  SaveFromHtmlTool.cs  ReplaceTextTool.cs
    GetSupportedFormatsTool.cs  GetFormFieldsTool.cs  FillFormFieldsTool.cs
    EditorFormats.cs            ← families, catalog, open/edit/save option factories (1-based → engine indexes)
    HtmlText.cs                 ← decoded text-node replacement and visible-text preview
    FormFields.cs               ← reading / setting legacy Word form fields
    EditorTool.cs               ← evaluation notice, engine-error translation, output naming, storing
    ToolError.cs                ← CallToolResult success/failure with the "<Op> failed for '<file>'" prefix
    TempWorkspace.cs            ← per-call temp folder; scrubs temp paths out of error text
src/GroupDocs.Editor.Mcp.Tests/ ← unit tests (no documents opened except the engine marker)
build/dependencies.props        ← SINGLE source of versions (server, engine, Core, security pins)
docker/  install/  changelog/  docs/backlog-and-issues.md  .github/workflows/
```

## Engine facts that shaped the code (GroupDocs.Editor 26.6.1)

- Worksheet/slide indexes are **0-based on edit, 1-based on save**; tool parameters are always 1-based.
- `PageRange.FromStartPageTillEndPage` treats the end as exclusive — use `FromStartPageWithCount`.
- Word documents must be opened with explicit `WordProcessingLoadOptions`, or `FormFieldManager`
  throws `NullReferenceException`. `EditorFormats.Open` always does this.
- Editable HTML keeps `&` raw but escapes `<`/`>`; replacement decodes text nodes, edits, re-encodes,
  and never touches tags, attributes, `<style>`, comments or `<head>`.
- CSV/TSV are Spreadsheet-family; Markdown is Textual; XPS is save-only; PPTX and email cannot be
  created from HTML alone.
- Evaluation mode: a banner in every output, an extra worksheet in saved workbooks, PDF editing
  limited to pages 1–2 (the engine's error for page 3+ is a misleading "file cannot be opened").

## Commands

```bash
dotnet build src/GroupDocs.Editor.Mcp.sln -c Release
dotnet test src/GroupDocs.Editor.Mcp.sln -c Release
pwsh ./build.ps1                                  # asserts server.json lockstep, packs into build_out/
pwsh install/generate-install-links.ps1 -Check    # CI drift guard for install/generated + README buttons
# integration suite against this build:
MCP_SERVER_DLL=$PWD/src/GroupDocs.Editor.Mcp/bin/Release/net10.0/GroupDocs.Editor.Mcp.dll \
  dotnet test ../GroupDocs.Editor.Mcp.Tests -c Release --filter "Category!=Metered"
```

## House rules

1. **Every change takes a CalVer bump** (`YY.M.N`) in `build/dependencies.props` **and** both `version`
   fields of `.mcp/server.json`, plus a `changelog/NNN-<version>-<slug>.md` entry — here and in the Tests repo.
2. **Tool descriptions are the product surface.** Each states the task, formats, "Do NOT pre-check",
   the response shape and the failure prefix. Change the integration tests when you change them.
3. **Failures return `ToolError.Failure`** (isError + prefix); never let an engine exception escape a tool
   after the file is resolved, and never put a temp path in a message.
4. **No silent success.** An operation that changed nothing says so and writes no file.
5. **Never commit** licence files, keys, `build_out/`, or machine paths. Run the kit's hygiene sweep first.
