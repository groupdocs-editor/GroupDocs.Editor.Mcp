using System.Globalization;
using GroupDocs.Editor.Words.FieldManagement;

namespace GroupDocs.Editor.Mcp.Tools;

/// <summary>Reading and setting legacy Word form fields (FORMTEXT, FORMCHECKBOX, FORMDROPDOWN, …).</summary>
internal static class FormFields
{
    public static void RequireWord(string fileName)
    {
        if (EditorFormats.FamilyOfExtension(Path.GetExtension(fileName)) != EditorFormats.WordProcessing)
            throw new ToolInputException("form fields exist only in Word documents (DOCX, DOC, DOCM, DOT, DOTX, RTF, ODT).");
    }

    /// <summary>The value as JSON-friendly data: string, bool, number, ISO date, or { options, selected }.</summary>
    public static object? ValueOf(IFormField field) => field switch
    {
        TextFormField t => t.Value,
        CheckBoxForm c => c.Value,
        NumberFormField n => n.Value,
        DateFormField d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        CurrentDateFormField d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        CurrentTimeFormField t => t.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        DropDownFormField dd => new
        {
            options = dd.Value ?? [],
            selected = dd.Value is { Count: > 0 } && dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Value.Count
                ? dd.Value[dd.SelectedIndex]
                : null
        },
        _ => null
    };

    /// <summary>Sets a field from text. Returns null on success, or why the value does not fit.</summary>
    public static string? TrySet(IFormField field, string value)
    {
        switch (field)
        {
            case TextFormField t:
                t.Value = value;
                return null;

            case CheckBoxForm c:
                var v = value.Trim().ToLowerInvariant();
                if (v is "true" or "yes" or "y" or "1" or "on" or "checked" or "x") { c.Value = true; return null; }
                if (v is "false" or "no" or "n" or "0" or "off" or "unchecked" or "") { c.Value = false; return null; }
                return $"'{value}' is not a check-box value (use true/false)";

            case NumberFormField n:
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { n.Value = number; return null; }
                return $"'{value}' is not a number";

            case DateFormField d:
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)) { d.Value = date; return null; }
                return $"'{value}' is not a date (use yyyy-MM-dd)";

            case DropDownFormField dd:
                var options = dd.Value ?? [];
                var index = options.FindIndex(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
                if (index < 0 && int.TryParse(value, out var i) && i >= 0 && i < options.Count) index = i;
                if (index >= 0) { dd.SelectedIndex = index; return null; }
                return $"'{value}' is not one of the options: {string.Join(", ", options)}";

            default:
                return $"fields of type {field.Type} cannot be set";
        }
    }
}
