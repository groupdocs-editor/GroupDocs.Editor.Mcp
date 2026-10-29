# GroupDocs.Editor MCP Server

Edit Word, Excel, PowerPoint, PDF and text documents from Claude, Cursor, GitHub Copilot and any
other MCP client — replace text without losing formatting, round-trip a document (or one worksheet,
slide or PDF page range) through editable HTML, and fill Word form fields. Runs locally: documents
never leave your machine.

```bash
docker run --rm -i -v "$(pwd)/documents:/data" ghcr.io/groupdocs-editor/editor-net-mcp:latest
```

On a **Linux host**, run the container as your own user so it can write the edited files into the
folder you mounted (the image runs as a non-root user, uid 1654):

```bash
docker run --rm -i --user "$(id -u):$(id -g)" -v "$(pwd)/documents:/data" \
  ghcr.io/groupdocs-editor/editor-net-mcp:latest
```

JSON client configs run no shell, so put the numbers in: `"--user", "1000:1000"`. Docker Desktop on
Windows and macOS does not need this.

## Tools

- **get_document_info** — format family, format, pages / worksheets / slides, encryption, save targets
- **extract_html** — a document, or one worksheet, slide or PDF page range, as editable HTML in storage
- **save_from_html** — edited HTML back to DOCX, XLSX, PPTX, PDF, TXT, Markdown, EPUB and more; can splice one worksheet or slide back into the original
- **replace_text** — find and replace that keeps formatting; zero matches writes nothing
- **get_supported_formats** — what can be opened and saved
- **get_form_fields** / **fill_form_fields** — read and fill Word form fields
- **get_license_status** — licensing mode, metered consumption, engine version

## Configuration

| Variable | Purpose |
|---|---|
| `GROUPDOCS_MCP_STORAGE_PATH` | Folder for input and output inside the container (default `/data`) |
| `GROUPDOCS_MCP_OUTPUT_PATH` | Optional separate output folder |
| `GROUPDOCS_LICENSE_PATH` | Path to a mounted GroupDocs licence file; omit for evaluation mode |
| `GROUPDOCS_METERED_PUBLIC_KEY` / `GROUPDOCS_METERED_PRIVATE_KEY` | Metered (pay-per-use) licensing; needs outbound access to GroupDocs servers |

Without a licence every tool works; edited output starts with an evaluation banner, saved workbooks
gain an extra worksheet, and only the first 2 PDF pages, worksheets and slides can be edited.

- Tags: `latest` + an immutable version tag per release matching NuGet (e.g. `26.10.0`).
- Source and docs: https://github.com/groupdocs-editor/GroupDocs.Editor.Mcp
- NuGet (`dnx GroupDocs.Editor.Mcp --yes`): https://www.nuget.org/packages/GroupDocs.Editor.Mcp
