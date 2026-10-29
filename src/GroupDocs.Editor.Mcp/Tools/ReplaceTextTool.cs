using System.ComponentModel;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class ReplaceTextTool
{
    [McpServerTool(Name = "replace_text", Destructive = false), Description(
        "Finds and replaces text in a document and saves an edited copy, keeping the document's formatting, tables, images and layout. " +
        "Use it whenever the user asks to change, rename, update or correct wording in a Word, Excel, PowerPoint, PDF or text document. " +
        "Supports DOCX, DOC, RTF, ODT, XLSX, XLS, ODS, CSV, PPTX, PPT, ODP, PDF, TXT, Markdown, EPUB and more. " +
        "Spreadsheets and presentations are edited one worksheet or slide at a time (1-based, default 1) and every other part is kept. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a missing file returns the list of available files. " +
        "Matching is case-sensitive by default; set matchCase=false, wholeWord=true or regex=true as needed. A phrase split across " +
        "differently formatted runs is not matched — use extract_html and save_from_html for those edits. " +
        "Returns 'Replaced N occurrence(s) …' and the saved path ('Saved to: …') of '<name>_edited.<ext>'. " +
        "When nothing matches it returns 'Text replacement found no matches …' and writes no file. " +
        "On failure isError is set and the text starts with 'Text replacement failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> ReplaceText(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        FileInput file,
        [Description("Text to find (or a .NET regular expression when regex=true)")] string find,
        [Description("Replacement text; with regex=true it may use $1-style group references")] string replace,
        [Description("Case-sensitive matching (default true)")] bool matchCase = true,
        [Description("Match whole words only (default false)")] bool wholeWord = false,
        [Description("Treat 'find' as a regular expression (default false)")] bool regex = false,
        [Description("Spreadsheets only: worksheet to edit, 1-based (default 1)")] int? worksheet = null,
        [Description("Presentations only: slide to edit, 1-based (default 1)")] int? slide = null,
        [Description("Output format extension (default: same as the input), e.g. 'pdf' to also convert")] string? format = null,
        [Description("Output file name (the extension follows the output format). Default: '<name>_edited.<ext>'")] string? outputName = null,
        [Description("Password, for password-protected documents. The edited copy keeps the same password when its format can carry one (Word, workbook, presentation, PDF)")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        using var work = new TempWorkspace();
        var family = EditorFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName)) ?? "";

        try
        {
            var matcher = HtmlText.Matcher(find, matchCase, wholeWord, regex);
            var path = await work.SaveInputAsync(resolved);
            using var editor = EditorFormats.Open(path, password);
            var info = EditorTool.Info(editor, password);
            family = EditorFormats.FamilyOf(info.Format);

            // Refuse before any engine work: an unsavable target would otherwise fail after the edit.
            var inputExt = EditorFormats.Normalize(Path.GetExtension(resolved.FileName));
            var target = string.IsNullOrWhiteSpace(format) ? inputExt : EditorFormats.Normalize(format);
            if (!EditorFormats.SaveFormats(family).Contains(target))
                throw new ToolInputException(
                    $"a {family} document cannot be saved as '{target}'" +
                    (string.IsNullOrWhiteSpace(format) ? ", its own format; pass 'format'" : "") +
                    $". Targets: {string.Join(", ", EditorFormats.SaveFormats(family))}.");

            // replace_text edits the whole PDF and has no page selection, so the evaluation page cap
            // would fail it with the engine's misleading "cannot be opened".
            if (family == EditorFormats.FixedLayout && !licenseManager.IsLicensed && info.PageCount > 2)
                throw new ToolInputException(
                    $"Evaluation mode edits only the first 2 PDF pages and this PDF has {info.PageCount}. Set a license, " +
                    "or edit pages 1-2 with extract_html (pages='1-2') and save_from_html.");

            var (editOptions, part) = EditorFormats.EditOptions(
                family, path, info.PageCount, worksheet, slide, pages: null, pagination: null, skipImages: null, showHidden: true,
                licensed: licenseManager.IsLicensed);

            using var document = editOptions is null ? editor.Edit() : editor.Edit(editOptions);
            var (html, count) = HtmlText.Replace(document.GetEmbeddedHtml(), matcher, replace, regex);

            var where = part is null ? $"'{resolved.FileName}'" : $"{part} of '{resolved.FileName}'";
            if (count == 0)
            {
                // D7: an edit that changed nothing must say so and must not leave a file behind.
                return ToolError.Success(EditorTool.WithNotice(licenseManager,
                    $"Text replacement found no matches for '{find}' in {where}. No file was written."));
            }

            // Splice the edited part back into a full copy of the original when the target keeps the
            // original's family; otherwise the output holds the edited part only.
            var sameFamily = EditorFormats.FamilyOfExtension(target) == family;

            // A protected input keeps its password in any output that can carry one; one that cannot
            // (TXT, CSV, Markdown, …) is said to be unprotected, never silently.
            var wasProtected = password is not null && info.IsEncrypted;
            var protectAgain = wasProtected && EditorFormats.SupportsPassword(target) ? password : null;
            var protection = protectAgain is not null ? " (output keeps the password)"
                : wasProtected ? $" (the output is NOT password-protected: '{target}' cannot carry a password)"
                : string.Empty;
            var saveOptions = EditorFormats.SaveOptions(target, protectAgain,
                worksheet: sameFamily && part?.Kind == "worksheet" ? part.Number : null,
                slide: sameFamily && part?.Kind == "slide" ? part.Number : null);

            using var edited = EditableDocument.FromMarkup(html);
            var name = EditorTool.CallerOutputName(outputName, target)
                       ?? EditorTool.OutputName(resolved.FileName, "_edited", target);
            var temp = work.PathFor("out_" + name);
            editor.Save(edited, temp, saveOptions);

            var message = await EditorTool.StoreAsync(storage, output, temp, name,
                $"Replaced {count} occurrence(s) of '{find}' in {where}" + protection);
            return ToolError.Success(EditorTool.WithNotice(licenseManager, message));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Text replacement", resolved.FileName, EditorTool.Translate(ex, slide), subject: $"(find='{find}')",
                hint: EditorTool.Hint(ex, family, licenseManager.IsLicensed, slide));
        }
    }
}
