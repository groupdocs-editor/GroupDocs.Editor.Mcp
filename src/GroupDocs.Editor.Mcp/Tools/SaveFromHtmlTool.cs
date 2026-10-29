using System.ComponentModel;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class SaveFromHtmlTool
{
    [McpServerTool(Name = "save_from_html", Destructive = false), Description(
        "Saves edited HTML back as a real document: DOCX, DOC, RTF, ODT, XLSX, XLS, ODS, CSV, PPTX, PPT, ODP, PDF, XPS, TXT, " +
        "Markdown (md), MHTML, EPUB, MOBI, AZW3, EML or MSG. Use it after extract_html when the user has edited the HTML, " +
        "or to turn any HTML file in storage into a document. " +
        "To put ONE edited worksheet or slide back into the original workbook or presentation (keeping every other part), " +
        "pass the original as sourceFile together with the same 1-based worksheet or slide number (required for workbooks; " +
        "a presentation sourceFile without slide gives a new one-slide presentation). " +
        "PPTX and email targets always need sourceFile of the same kind. A resource folder named <name>_resources next to the .html is picked up automatically. " +
        "Returns the saved path ('Saved to: …'). The output name is the HTML's name with the new extension unless outputName is given; " +
        "an existing file is never overwritten (a ' (1)' suffix is added). " +
        "On failure isError is set and the text starts with 'Save from HTML failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> SaveFromHtml(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        [Description("The .html file to save, e.g. the file extract_html wrote")] FileInput htmlFile,
        [Description("Target format extension: docx, xlsx, pptx, pdf, txt, md, epub, odt, rtf, csv, xps, mhtml, …")] string format,
        [Description("Output file name (the extension is set from 'format'). Default: the HTML's name")] string? outputName = null,
        [Description("Password-protect the output (Word, spreadsheet, presentation and PDF targets)")] string? password = null,
        [Description("The original document to splice the edited part back into (needed for PPTX and email targets)")] FileInput? sourceFile = null,
        [Description("With sourceFile: which worksheet the HTML replaces, 1-based")] int? worksheet = null,
        [Description("With sourceFile: which slide the HTML replaces, 1-based")] int? slide = null,
        [Description("Password of sourceFile, if it is protected")] string? sourcePassword = null)
    {
        licenseManager.SetLicense();
        using var resolvedHtml = await resolver.ResolveAsync(htmlFile);
        using var resolvedSource = sourceFile is null ? null : await resolver.ResolveAsync(sourceFile);
        using var work = new TempWorkspace();

        try
        {
            var target = EditorFormats.Normalize(format);
            var targetFamily = EditorFormats.FamilyOfExtension(target);
            if ((worksheet is not null || slide is not null) && resolvedSource is null)
                throw new ToolInputException("'worksheet' and 'slide' need 'sourceFile' — the original to splice the edited part into.");
            if (worksheet is < 1 || slide is < 1)
                throw new ToolInputException("'worksheet' and 'slide' are 1-based.");
            if (worksheet is not null && (targetFamily != EditorFormats.Spreadsheet || EditorFormats.IsDelimited(target)))
                throw new ToolInputException($"'worksheet' applies to workbook targets (XLSX, XLS, ODS, …), not '{target}'.");
            if (slide is not null && targetFamily != EditorFormats.Presentation)
                throw new ToolInputException($"'slide' applies to presentation targets (PPTX, PPT, ODP, …), not '{target}'.");

            // Save options first: an unknown target fails before any engine work.
            var saveOptions = EditorFormats.SaveOptions(target, password, worksheet, slide);

            var htmlPath = await work.SaveInputAsync(resolvedHtml);
            var resourceFolder = await CopyResourceFolderAsync(storage, htmlFile, resolvedHtml.FileName, work);

            using var document = EditableDocument.FromFile(htmlPath, resourceFolder);

            Editor editor;
            var into = string.Empty;
            if (resolvedSource is not null)
            {
                var sourcePath = await work.SaveInputAsync(resolvedSource, "source_" + resolvedSource.FileName);
                editor = EditorFormats.Open(sourcePath, sourcePassword);
                try
                {
                    into = CheckSplice(EditorTool.Info(editor, sourcePassword), resolvedSource.FileName, sourcePath, target, targetFamily, worksheet, slide);
                }
                catch
                {
                    editor.Dispose();
                    throw;
                }
            }
            else if (EditorFormats.CanCreateBlank(target))
            {
                editor = new Editor(EditorFormats.BlankFormatFor(target));
            }
            else
            {
                throw new ToolInputException(
                    $"'{target}' output cannot be created from HTML alone; pass the original document as 'sourceFile'.");
            }

            using (editor)
            {
                var name = EditorTool.CallerOutputName(outputName, target)
                           ?? EditorTool.OutputName(resolvedHtml.FileName, string.Empty, target);
                var temp = work.PathFor("out_" + name);
                editor.Save(document, temp, saveOptions);

                var what = $"Saved '{resolvedHtml.FileName}' {(into.Length > 0 ? into : "as " + target.ToUpperInvariant())}";
                if (password is not null) what += ", password-protected";

                var message = await EditorTool.StoreAsync(storage, output, temp, name, what);
                return ToolError.Success(EditorTool.WithNotice(licenseManager, message));
            }
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Save from HTML", resolvedHtml.FileName, ex, subject: $"(-> {format})",
                hint: EditorTool.Hint(ex, "", licenseManager.IsLicensed));
        }
    }

    /// <summary>
    /// Checks that <c>sourceFile</c> is used for something the engine really does with it, and returns
    /// the words that say what happened. The engine silently clamps an out-of-range worksheet or slide
    /// number to the last one, and slide number 0 (omitted) means "a new presentation" — both measured
    /// on 26.6.1 — so neither may reach it unchecked.
    /// </summary>
    private static string CheckSplice(GroupDocs.Editor.Metadata.IDocumentInfo info, string sourceName, string sourcePath,
        string target, string? targetFamily, int? worksheet, int? slide)
    {
        var sourceFamily = EditorFormats.FamilyOf(info.Format);
        var upper = target.ToUpperInvariant();

        if (sourceFamily == EditorFormats.Spreadsheet && targetFamily == EditorFormats.Spreadsheet && !EditorFormats.IsDelimited(target))
        {
            if (EditorFormats.IsDelimited(sourcePath))
                throw new ToolInputException($"'{sourceName}' is a delimited text file with no worksheets to splice into; omit 'sourceFile'.");
            if (worksheet is not { } w)
                throw new ToolInputException($"pass 'worksheet' — the 1-based worksheet of '{sourceName}' the HTML replaces.");
            EditorFormats.CheckPart("worksheet", w, info.PageCount);
            return $"into a copy of '{sourceName}', replacing worksheet {w} of {info.PageCount} ({upper})";
        }

        if (sourceFamily == EditorFormats.Presentation && targetFamily == EditorFormats.Presentation)
        {
            if (slide is not { } s)
                return $"as a new one-slide {upper} ('{sourceName}' supplied the presentation format only; " +
                       "pass 'slide' to replace one slide in a copy of it instead)";
            EditorFormats.CheckPart("slide", s, info.PageCount);
            return $"into a copy of '{sourceName}', replacing slide {s} of {info.PageCount} ({upper})";
        }

        if (sourceFamily == EditorFormats.Email && targetFamily == EditorFormats.Email)
            return $"as {upper}, using '{sourceName}' as the original message";

        throw new ToolInputException(
            $"'sourceFile' is used to replace one worksheet or slide in a copy of a workbook or presentation of the target's kind, " +
            $"or as the original message for email output. '{sourceName}' is a {sourceFamily} document and the target is '{target}'" +
            (EditorFormats.CanCreateBlank(target) ? $"; omit 'sourceFile' to create the {upper} from the HTML alone." : "."));
    }

    /// <summary>
    /// Copies "&lt;name&gt;_resources/" from beside the HTML in storage into the workspace, so CSS, images and
    /// fonts referenced relatively resolve. Returns the local folder, or null when there is none.
    /// </summary>
    private static async Task<string?> CopyResourceFolderAsync(IFileStorage storage, FileInput htmlFile, string htmlName, TempWorkspace work)
    {
        var stored = htmlFile.FilePath ?? (htmlFile.FileContent is null ? htmlFile.FileName : null);
        if (string.IsNullOrEmpty(stored)) return null;

        var dir = Path.GetDirectoryName(stored.Replace('\\', '/')) ?? string.Empty;
        var folderName = Path.GetFileNameWithoutExtension(htmlName) + "_resources";
        var folder = string.IsNullOrEmpty(dir) ? folderName : $"{dir}/{folderName}";

        var entries = (await storage.ListDirsAndFilesAsync(folder)).Where(e => !e.IsDirectory).ToList();
        if (entries.Count == 0) return null;

        var local = Path.Combine(work.Root, folderName);
        Directory.CreateDirectory(local);
        foreach (var entry in entries)
        {
            await using var source = await storage.ReadFileStreamAsync(entry.FilePath);
            await using var target = File.Create(Path.Combine(local, entry.FileName));
            await source.CopyToAsync(target);
        }
        return local;
    }
}
