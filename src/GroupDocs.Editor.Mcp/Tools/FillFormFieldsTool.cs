using System.ComponentModel;
using System.Text;
using GroupDocs.Editor.Formats;
using GroupDocs.Editor.Options;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Editor.Mcp.Tools;

[McpServerToolType]
public static class FillFormFieldsTool
{
    [McpServerTool(Name = "fill_form_fields", Destructive = false), Description(
        "Fills in the form fields of a Word document and saves a filled copy, leaving the rest of the document untouched. " +
        "Use it when the user asks to complete, fill out or tick boxes in a Word form. Call get_form_fields first for the exact field names. " +
        "Supports DOCX, DOC, DOCM, DOT, DOTX, RTF and ODT. " +
        "Do NOT pre-check whether files exist — just pass the filename the user provided; a missing file returns the list of available files. " +
        "values maps field name to text: any text for text fields, true/false for check boxes, an option (or its 0-based index) for drop-downs, " +
        "a number for number fields, yyyy-MM-dd for date fields. Every field with that name is set. " +
        "Returns 'Updated N field(s)', any names that were not found or values that did not fit, and the saved path ('Saved to: …') " +
        "of '<name>_edited.<ext>'. When no field could be updated no file is written. " +
        "On failure isError is set and the text starts with 'Form-field update failed for'; a file that cannot be found returns the list of available files instead.")]
    public static async Task<CallToolResult> FillFormFields(
        IFileResolver resolver,
        IFileStorage storage,
        ILicenseManager licenseManager,
        OutputHelper output,
        FileInput file,
        [Description("Field name -> value, e.g. { \"Text1\": \"Jane Doe\", \"Check1\": \"true\" }")] Dictionary<string, string> values,
        [Description("Output file name (default '<name>_edited.<ext>')")] string? outputName = null,
        [Description("Output format: docx (default: same as input), doc, docm, rtf, odt, …")] string? format = null,
        [Description("Password, for password-protected documents. The filled copy keeps the same password")] string? password = null)
    {
        licenseManager.SetLicense();
        using var resolved = await resolver.ResolveAsync(file);
        using var work = new TempWorkspace();

        try
        {
            FormFields.RequireWord(resolved.FileName);
            if (values is null || values.Count == 0)
                throw new ToolInputException("'values' must name at least one field.");

            var target = string.IsNullOrWhiteSpace(format)
                ? EditorFormats.Normalize(Path.GetExtension(resolved.FileName))
                : EditorFormats.Normalize(format);
            if (WordProcessingFormats.FromExtension(target) is not { } targetFormat)
                throw new ToolInputException($"a filled form is saved as a Word document; '{target}' is not one.");

            var path = await work.SaveInputAsync(resolved);
            using var editor = EditorFormats.Open(path, password);
            var info = EditorTool.Info(editor, password);
            var manager = editor.FormFieldManager;
            var collection = manager.FormFieldCollection;
            var fields = collection.ToList();

            var updated = new List<string>();
            var unknown = new List<string>();
            var rejected = new List<string>();

            foreach (var (fieldName, value) in values)
            {
                var matches = fields.Where(f => string.Equals(f.Name, fieldName, StringComparison.Ordinal)).ToList();
                if (matches.Count == 0) { unknown.Add(fieldName); continue; }

                var errors = matches.Select(f => FormFields.TrySet(f, value ?? string.Empty)).Where(e => e is not null).ToList();
                if (errors.Count > 0) rejected.Add($"{fieldName}: {errors[0]}");
                else updated.Add(fieldName);
            }

            var report = new StringBuilder($"Updated {updated.Count} field(s) in '{resolved.FileName}'");
            if (updated.Count > 0) report.Append(": ").Append(string.Join(", ", updated));
            report.Append('.');
            if (unknown.Count > 0) report.Append($"\nNot found: {string.Join(", ", unknown)}. Call get_form_fields for the exact names.");
            if (rejected.Count > 0) report.Append($"\nNot set: {string.Join("; ", rejected)}.");

            if (updated.Count == 0)
            {
                report.Append("\nNo file was written.");
                return ToolError.Success(EditorTool.WithNotice(licenseManager, report.ToString()));
            }

            manager.UpdateFormFiled(collection);   // [sic] — the engine's spelling

            var keepPassword = info.IsEncrypted ? password : null;
            var name = EditorTool.CallerOutputName(outputName, target)
                       ?? EditorTool.OutputName(resolved.FileName, "_edited", target);
            var temp = work.PathFor("out_" + name);
            using (var stream = File.Create(temp))
                editor.Save(stream, new WordProcessingSaveOptions(targetFormat) { Password = keepPassword });

            var message = await EditorTool.StoreAsync(storage, output, temp, name, report.ToString());
            return ToolError.Success(EditorTool.WithNotice(licenseManager, message));
        }
        catch (Exception ex)
        {
            return ToolError.Failure("Form-field update", resolved.FileName, ex,
                hint: EditorTool.Hint(ex, EditorFormats.WordProcessing, licenseManager.IsLicensed));
        }
    }
}
