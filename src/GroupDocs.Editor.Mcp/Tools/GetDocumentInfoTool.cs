using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class GetDocumentInfoTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_document_info", ReadOnly = true, Idempotent = true), Description(
        "Inspects a document before editing it: its format family, format, number of pages (worksheets for spreadsheets, " +
        "slides for presentations), size, whether it is password-protected, and which formats it can be saved to. " +
        "Supports DOCX, DOC, RTF, ODT, XLSX, XLS, ODS, CSV, PPTX, PPT, ODP, PDF, TXT, HTML, XML, Markdown, EPUB, MOBI, EML, MSG and more. " +
        "Call this first when the user asks how many pages, worksheets or slides a document has, or whether it can be edited. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a missing file returns the list of available files. " +
        "Returns JSON: { fileName, family, format: { name, extension, mime }, pageCount, size, isEncrypted, infoType, canEdit, canSaveTo[] }. " +
        "family is one of WordProcessing, Spreadsheet, Presentation, FixedLayout, Textual, Email, Ebook. " +
        "On failure isError is set and the text starts with 'Document-info lookup failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> GetDocumentInfo(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        FileInput file,
        [Description("Password, for password-protected documents")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        using var work = new TempWorkspace();

        try
        {
            var path = await work.SaveInputAsync(resolved);
            using var editor = EditorFormats.Open(path, password);
            var info = EditorTool.Info(editor, password);
            var family = EditorFormats.FamilyOf(info.Format);

            var payload = new
            {
                fileName = resolved.FileName,
                family,
                format = new
                {
                    name = info.Format.Name,
                    extension = EditorFormats.Normalize(info.Format.Extension),
                    mime = info.Format.Mime
                },
                pageCount = info.PageCount,
                size = info.Size,
                isEncrypted = info.IsEncrypted,
                infoType = info.GetType().Name,
                canEdit = true,
                canSaveTo = EditorFormats.SaveFormats(family)
            };

            return ToolError.Success(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Document-info lookup", resolved.FileName, ex,
                hint: EditorTool.Hint(ex, EditorFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName)) ?? "", licenseManager.IsLicensed));
        }
    }
}
