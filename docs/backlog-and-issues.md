# GroupDocs.Editor.Mcp — backlog and known issues

Product-level backlog for the server. `M#` = fix in this repo or GroupDocs.Mcp.Core, `P#` = behaviour of the
GroupDocs.Editor engine (26.6.1) that the server works around or documents.

## Confirmed defects

*None open at first release.*

## Known issues & limitations

- [ ] **P1 · Evaluation-mode PDF cap reports a misleading error — Med.** Editing PDF page 3 or later
      unlicensed throws `FileLoadException: The file cannot be opened. It might have unsupported format or
      be corrupted.` The server appends "Evaluation mode edits only the first 2 PDF pages" to the error.
- [ ] **P2 · Evaluation output is altered — Info.** Every output starts with an evaluation banner and a
      saved workbook gains an extra worksheet. Documented in README / how-to; tests assert lower bounds.
- [ ] **P3 · Form fields need explicit load options — Info.** `FormFieldManager.FormFieldCollection` throws
      `NullReferenceException` unless the editor was opened with `WordProcessingLoadOptions`.
      `EditorFormats.Open` always passes them.
- [ ] **P4 · `FixInvalidFormFieldNames` does not clear duplicate names — Low.** On the upstream legacy-forms
      sample, fixing 15 invalid names left `HasInvalidFormFields()` true. Filling works regardless, so v1 has
      no `fixInvalidNames` parameter; `get_form_fields` reports `hasInvalidFormFields`.
- [ ] **P5 · Hidden-slide message is 0-based — Low.** "Requested slide #1 is hidden" refers to 0-based slide 1
      (the second slide). The server rewrites it to the 1-based number.
- [ ] **P6 · Content sniffing — Info.** A `.txt` file that contains a URL is detected as HTML.
- [ ] **P7 · No PPTX / email from scratch — Info.** Saving HTML to PPTX fails with "Please attach original
      stylesheet"; to EML with "invalid HTML document". `save_from_html` requires `sourceFile` for those.
- [ ] **P8 · Remote resources — Med.** The engine may fetch remote images/stylesheets referenced by HTML or
      Office input while loading, with no switch to disable it (kit network-access verdict). Documented.
- [ ] **P9 · Evaluation mode reaches only parts 1-2 and silently clamps — High (guarded).** Unlicensed, slide 3
      comes back byte-identical to slide 2, and worksheet 3 as worksheet 2; the engine still reports success.
      `extract_html` and `replace_text` refuse worksheet/slide 3+ unlicensed with an explanation. Saving into
      part 3+ is not capped.
- [ ] **P10 · Hidden worksheets select the wrong sheet — Med (guarded).** With `ExcludeHiddenWorksheets=true`
      `WorksheetIndex` counts visible sheets only; with `false`, asking for a hidden sheet returns worksheet 1.
      For XLSX/XLSM/XLTX/XLTM the server reads the sheet states, maps the 1-based number (which counts every
      sheet, like the save side) to its visible position, and refuses a hidden worksheet. XLS/ODS/XLSB states
      are not readable, so numbering there is only correct for workbooks without hidden sheets.
- [ ] **P11 · Out-of-range part numbers are clamped on save — Info (guarded).** `SlideNumber`/`WorksheetNumber`
      beyond the last part replace the last part, and 0 (omitted) means "new presentation". `save_from_html`
      range-checks against the source and words the result for what really happened.
- [ ] **M1 · Phrases split across formatting runs are not matched by `replace_text` — Med.** Documented in
      the tool description; the general path is `extract_html` + `save_from_html`.

## Tools & functionality

- [ ] **P1** `replace_text` across all worksheets / slides in one call.
- [ ] **P1** Preview tool (`generate_preview`): page/worksheet/slide SVG via `*DocumentInfo.GeneratePreview(int)`.
- [ ] **P2** `delete_worksheet` / `delete_slide` (`WorksheetNumbersToDelete`, `SlideNumbersToDelete`).
- [ ] **P2** Document protection on save (`WordProcessingProtection`, `WorksheetProtection`).
- [ ] **P2** Run-merging so `replace_text` can match a phrase split across formatting runs.
- [ ] **P3** Email-specific editing options (`MailMessageOutput` selection).

## Testing & CI

- [ ] **P1** First docker-channel run against the published GHCR image (needs the first release).
- [ ] **P1** `MeteredLicensingTests`: add a billable `replace_text` asserting consumption increases once a
      metered CI account exists.
- [ ] **P2** Licensed-mode run (optional `GROUPDOCS_LICENSE` secret) to confirm the banner, extra worksheet
      and PDF cap disappear.

## Documentation & discoverability

- [ ] **P1** Docs section `docs.groupdocs.com/editor/mcp/`, announcement post, forum thread, landing-page MCP tab.
- [ ] **P2** 30-second demo recording for the README.

## Platform & infra

- [ ] **P2** `Microsoft.Build.Tasks.Git` 8.0.0 (via `Microsoft.SourceLink.GitHub` 8.0.0) is flagged by NuGet
      audit (GHSA-23fw-v26w-5fgq). Build-time only and family-wide; fix with the family SourceLink bump.
- [ ] **P2** Drop the `System.Security.Cryptography.Pkcs` pin once the engine depends on a patched version.
