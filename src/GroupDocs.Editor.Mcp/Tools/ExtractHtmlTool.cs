using System.ComponentModel;
using System.Text;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class ExtractHtmlTool
{
    [McpServerTool(Name = "extract_html", Destructive = false), Description(
        "Opens a document for editing: converts it — or one worksheet, one slide, or a PDF page range — into editable HTML " +
        "and saves the .html file to storage, keeping fonts, styles, tables and images. Edit that file, then call save_from_html " +
        "to turn it back into DOCX, XLSX, PPTX, PDF or another format. For a simple find-and-replace use replace_text instead. " +
        "Supports DOCX, DOC, RTF, ODT, XLSX, XLS, ODS, CSV, PPTX, PPT, ODP, PDF, TXT, HTML, XML, Markdown, EPUB, MOBI, EML and more. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a missing file returns the list of available files. " +
        "By default the HTML is self-contained (images, CSS and fonts embedded); set embedResources=false to get <name>.html plus a <name>_resources folder. " +
        "Returns the saved path ('Saved to: …'), which part was extracted (e.g. 'worksheet 2 of 3'), and a plain-text preview of the content. " +
        "On failure isError is set and the text starts with 'HTML extraction failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> ExtractHtml(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        FileInput file,
        [Description("Password, for password-protected documents")] string? password = null,
        [Description("Spreadsheets only: which worksheet to extract, 1-based (default 1)")] int? worksheet = null,
        [Description("Presentations only: which slide to extract, 1-based (default 1)")] int? slide = null,
        [Description("PDF only: 1-based page or inclusive range, e.g. '3' or '2-4' (default: all pages)")] string? pages = null,
        [Description("Word, PDF and text: true keeps page boundaries (paginal HTML); false (default) gives one continuous flow")] bool? pagination = null,
        [Description("PDF only: true leaves images out and keeps text only")] bool? skipImages = null,
        [Description("true (default) embeds images, CSS and fonts in one .html file; false writes a <name>_resources folder beside it")] bool embedResources = true,
        [Description("Spreadsheets and presentations: allow hidden worksheets and slides")] bool showHidden = false)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        using var work = new TempWorkspace();
        var family = EditorFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName)) ?? "";

        try
        {
            var path = await work.SaveInputAsync(resolved);
            using var editor = EditorFormats.Open(path, password);
            var info = EditorTool.Info(editor, password);
            family = EditorFormats.FamilyOf(info.Format);

            var (options, part) = EditorFormats.EditOptions(
                family, path, info.PageCount, worksheet, slide, pages, pagination, skipImages, showHidden, licenseManager.IsLicensed);

            using var document = options is null ? editor.Edit() : editor.Edit(options);

            var baseName = Path.GetFileNameWithoutExtension(resolved.FileName) + PartSuffix(part, pages);
            string savedMessage;
            string html;

            if (embedResources)
            {
                html = document.GetEmbeddedHtml();
                var temp = work.PathFor(baseName + ".html");
                await File.WriteAllTextAsync(temp, html, new UTF8Encoding(false));
                savedMessage = await EditorTool.StoreAsync(storage, output, temp, baseName + ".html", Describe(resolved.FileName, part));
            }
            else
            {
                // The HTML references "<base>_resources/…", so the pair must share one base name. Reserve
                // the .html name first — storage de-duplicates it in the folder it really writes to (the
                // output folder, which may differ from the one tools list) — then render under that name.
                var reserved = await storage.WriteFileAsync(baseName + ".html", [], rewrite: false);
                baseName = Path.GetFileNameWithoutExtension(reserved);
                var tempHtml = work.PathFor(baseName + ".html");
                document.Save(tempHtml);
                html = await File.ReadAllTextAsync(tempHtml);

                var resourceDir = Path.Combine(work.Root, baseName + "_resources");
                if (Directory.Exists(resourceDir))
                {
                    // The .html name was free, so this folder belongs to it; only an orphaned folder left
                    // behind by a deleted .html can be overwritten here.
                    foreach (var resource in Directory.EnumerateFiles(resourceDir))
                    {
                        var bytes = await File.ReadAllBytesAsync(resource);
                        await storage.WriteFileAsync($"{baseName}_resources/{Path.GetFileName(resource)}", bytes, rewrite: true);
                    }
                }

                savedMessage = await EditorTool.StoreAsync(storage, output, tempHtml, baseName + ".html",
                    Describe(resolved.FileName, part) + $" (resources in '{baseName}_resources/')", rewrite: true);
            }

            var preview = output.TruncateText(
                HtmlText.VisibleText(html),
                "The saved .html holds the full content.");

            var text = new StringBuilder()
                .AppendLine(savedMessage)
                .AppendLine()
                .AppendLine("Next: edit the saved .html and call save_from_html to write it back as DOCX, XLSX, PPTX, PDF or another format.")
                .AppendLine()
                .AppendLine("Preview (plain text):")
                .Append(preview)
                .ToString();

            return ToolError.Success(EditorTool.WithNotice(licenseManager, text));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("HTML extraction", resolved.FileName, EditorTool.Translate(ex, slide),
                hint: EditorTool.Hint(ex, family, licenseManager.IsLicensed, slide));
        }
    }

    private static string Describe(string fileName, EditorFormats.Part? part) =>
        part is null
            ? $"Extracted '{fileName}' as editable HTML"
            : $"Extracted {part} of '{fileName}' as editable HTML";

    private static string PartSuffix(EditorFormats.Part? part, string? pages) =>
        part is not null ? $"_{part.Kind}{part.Number}"
        : pages is not null ? $"_pages{pages.Replace(" ", "")}"
        : string.Empty;
}
