using System.ComponentModel;
using System.Text.Json;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class GetFormFieldsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_form_fields", ReadOnly = true, Idempotent = true), Description(
        "Lists the fillable form fields in a Word document — text boxes, check boxes, drop-downs, number and date fields — " +
        "with each field's name, type and current value. Call it before fill_form_fields to learn the exact field names. " +
        "Supports DOCX, DOC, DOCM, DOT, DOTX, RTF and ODT. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a missing file returns the list of available files. " +
        "Returns JSON: { fileName, count, hasInvalidFormFields, fields: [ { name, type, value } ] }. type is Text, CheckBox, " +
        "DropDown, Number, Date, CurrentDate or CurrentTime; a check box value is true/false; a drop-down value is { options[], selected }. " +
        "hasInvalidFormFields is true when names are duplicated or malformed. " +
        "On failure isError is set and the text starts with 'Form-field lookup failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> GetFormFields(
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
            FormFields.RequireWord(resolved.FileName);
            var path = await work.SaveInputAsync(resolved);
            using var editor = EditorFormats.Open(path, password);   // Word → explicit load options (required by the engine)

            var manager = editor.FormFieldManager;
            var fields = manager.FormFieldCollection
                .Select(f => new { name = f.Name, type = f.Type.ToString(), value = FormFields.ValueOf(f) })
                .ToList();

            var payload = new
            {
                fileName = resolved.FileName,
                count = fields.Count,
                hasInvalidFormFields = fields.Count > 0 && manager.HasInvalidFormFields(),
                fields
            };
            return ToolError.Success(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Form-field lookup", resolved.FileName, ex,
                hint: EditorTool.Hint(ex, EditorFormats.WordProcessing, licenseManager.IsLicensed));
        }
    }
}
