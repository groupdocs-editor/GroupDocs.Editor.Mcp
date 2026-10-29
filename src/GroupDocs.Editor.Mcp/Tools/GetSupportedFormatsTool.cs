using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class GetSupportedFormatsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_supported_formats", ReadOnly = true, Idempotent = true), Description(
        "Lists the formats GroupDocs.Editor can open for editing and save to. Without a file it returns the whole catalog per " +
        "format family; with a file it returns that file's family and the formats it can be saved to. " +
        "Call this when the user asks which formats are supported, or before save_from_html / replace_text with an unusual target. " +
        "Families: WordProcessing, Spreadsheet, Presentation, FixedLayout (PDF), Textual (TXT, HTML, XML, Markdown, JSON), Email, Ebook. " +
        "Returns JSON: without a file { families: [ { family, loadFormats[], saveFormats[] } ] }; with a file { fileName, family, saveFormats[] }. " +
        "Extensions are lower-case without a dot. " +
        "On failure isError is set and the text starts with 'Supported-formats lookup failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> GetSupportedFormats(
        IFileResolver resolver,
        ILicenseManager licenseManager,
        [Description("Optional: a document to list save targets for")] FileInput? file = null)
    {
        if (file is null || (file.FilePath is null && file.FileName is null && file.FileContent is null))
        {
            var catalog = new
            {
                families = EditorFormats.Families.Select(f => new
                {
                    family = f,
                    loadFormats = EditorFormats.LoadFormats(f),
                    saveFormats = EditorFormats.SaveFormats(f)
                })
            };
            return ToolError.Success(JsonSerializer.Serialize(catalog, JsonOptions));
        }

        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);

        try
        {
            var family = EditorFormats.FamilyOfExtension(Path.GetExtension(resolved.FileName));
            if (family is null)
            {
                // Unknown extension: let the engine sniff the content.
                using var work = new TempWorkspace();
                var path = await work.SaveInputAsync(resolved);
                using var editor = new Editor(path);
                family = EditorFormats.FamilyOf(EditorTool.Info(editor, null).Format);
            }

            var payload = new
            {
                fileName = resolved.FileName,
                family,
                saveFormats = EditorFormats.SaveFormats(family)
            };
            return ToolError.Success(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Supported-formats lookup", resolved.FileName, ex);
        }
    }
}
