---
id: 001
date: 2026-10-29
version: 26.10.0
type: feature
---

# Initial release of GroupDocs.Editor.Mcp

## What changed

- New MCP server for GroupDocs.Editor for .NET **26.6.1** on **GroupDocs.Mcp.Core 26.9.0**, cloned in
  shape from GroupDocs.Conversion.Mcp 26.9.0 (workflows, `install/` generator, Docker, build and release scripts).
- Seven tools, plus Core's `get_license_status`:
  - `get_document_info` — family, format, pages/worksheets/slides, size, encryption, save targets (JSON).
  - `extract_html` — a document, or one worksheet, slide or PDF page range, to editable HTML in storage;
    self-contained by default or with a `<name>_resources` folder; plain-text preview.
  - `save_from_html` — HTML back to DOCX, XLSX, PPTX, PDF, TXT, Markdown, EPUB and more; splices one
    edited worksheet or slide back into `sourceFile`; optional password and output name.
  - `replace_text` — find/replace on decoded text nodes inside `<body>` (literal, case-insensitive,
    whole word, regex), one worksheet or slide at a time, optional output format; zero matches writes nothing.
  - `get_supported_formats` — load/save catalog per family, or save targets for one file.
  - `get_form_fields` / `fill_form_fields` — legacy Word form fields; unknown names and unfit values reported.
- Tools return `CallToolResult`: every failure sets `isError` **and** starts with the tool's prefix
  (`HTML extraction failed for '<file>': …`). Temp paths are scrubbed from engine messages.
- Licensing: `EditorLicenseManager` implements licence file, metered key and consumption reading, and
  declares the engine for `get_license_status`.
- Package: nuget-first, 174 MB nupkg (measured), `McpServer` package type, native PDBs stripped.

## Decisions

- **Native dependencies:** none beyond fonts. GroupDocs.Editor 26.6.1 uses Aspose.Drawing and declares
  SkiaSharp + `SkiaSharp.NativeAssets.Linux.NoDependencies` itself, so there is no
  `System.Drawing.EnableUnixSupport`, no `libgdiplus`, and no manual SkiaSharp pin. The Docker image
  installs `libfontconfig1`, `fontconfig` (for `fc-cache`) and `ttf-mscorefonts-installer` for PDF/XPS output.
- **Security pin:** `System.Security.Cryptography.Pkcs` 10.0.13 overrides the engine's transitive 8.0.0
  (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- **Docker on Linux:** README and Docker Hub README document `--user "$(id -u):$(id -g)"` from day one. A write the
  container user is denied (the same cause) fails with that fix in the message, not only the raw
  `UnauthorizedAccessException` — the lesson of GroupDocs.Search.Mcp 26.10.1.

## Why

AI agents are being asked to change Office files "without wrecking the formatting"; GroupDocs had no
MCP server for its editing engine. This one targets that intent directly.

## Fixed before the first release (code review 2026-10-29)

Every item below was reproduced against the local build first.

- **Evaluation mode never edits the wrong part.** Unlicensed, the engine returns slide 2 for slide 3 (and
  worksheet 2 for worksheet 3) and reports success; `replace_text` would then have spliced that edit back.
  `extract_html` and `replace_text` now refuse worksheet/slide 3+ without a licence and say why.
- **Hidden worksheets.** Worksheet numbers count every sheet, as the save side does; for Office Open XML
  workbooks hidden sheets are mapped around and a hidden worksheet is refused rather than silently swapped
  for worksheet 1.
- **`save_from_html` splicing.** A worksheet/slide number beyond the source is refused (the engine replaced
  the last part); a workbook `sourceFile` needs `worksheet`; a presentation `sourceFile` without `slide` is
  reported as a new one-slide deck, not "a copy of" the source; a `sourceFile`, `worksheet` or `slide` that
  does not fit the target is refused.
- **`extract_html` with `embedResources=false`** reserves the `.html` name in the folder storage really writes
  to, so a second extraction gets `name (1).html` + `name (1)_resources/` instead of overwriting the first
  extraction's resources (wrong when `GROUPDOCS_MCP_OUTPUT_PATH` was set).
- **`replace_text`** keeps a protected input's password in any output that can carry one (PDF included) and
  says plainly when the target cannot; refuses an input it cannot save back (HTML, XML) before editing; and
  explains the evaluation PDF page cap up front instead of failing with "the file cannot be opened".
- **Regex** find patterns get a 15 s budget for the whole document, not 5 s per text node.
- Engine messages name the file they are about (`sourceFile` vs the HTML), and tool descriptions no longer
  promise the failure prefix for a missing file (that case returns the list of available files).

## Verification

- Unit tests: 93/93 (`dotnet test src/GroupDocs.Editor.Mcp.sln`).
- Integration suite (GroupDocs.Editor.Mcp.Tests, 80 tests) in evaluation mode: 80/80 against this build via
  `MCP_SERVER_DLL` on Windows, and 80/80 against the locally built image via `MCP_SERVER_IMAGE` (Linux container).
- Installer verifier (`verify-groupdocs-mcp.ps1`, handshake + auto) against the local image; a sequenced
  dnx session against the packed `.nupkg`.
- `build.ps1`: server.json lockstep holds; `install/generate-install-links.ps1 -Check` clean.

## Migration / impact

None — first release.
