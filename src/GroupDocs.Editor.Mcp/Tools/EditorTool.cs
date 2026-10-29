using System.Text.RegularExpressions;
using GroupDocs.Editor.Metadata;
using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;

namespace GroupDocs.Editor.Mcp.Tools;

/// <summary>Helpers shared by the tool classes.</summary>
internal static class EditorTool
{
    /// <summary>
    /// One line, prefixed to the text of every editing tool when unlicensed. Spelled "Evaluation mode:"
    /// (not "[Evaluation mode]") because some clients locate structured output by its first '['.
    /// Measured on GroupDocs.Editor 26.6.1; see the build-out plan, section 8.3.
    /// </summary>
    public const string EvaluationNotice =
        "Evaluation mode: outputs start with an evaluation banner, saved workbooks gain an extra worksheet, " +
        "and only the first 2 PDF pages, worksheets and slides can be edited. Set GROUPDOCS_LICENSE_PATH, or the metered keys, to remove these limits.";

    public static string WithNotice(ILicenseManager license, string text) =>
        license.IsLicensed ? text : EvaluationNotice + "\n\n" + text;

    /// <summary>Reads document info; the password is needed for protected files.</summary>
    public static IDocumentInfo Info(Editor editor, string? password) =>
        editor.GetDocumentInfo(password)
        ?? throw new ToolInputException("this is not a format GroupDocs.Editor can open. Call get_supported_formats for the list.");

    /// <summary>
    /// Replaces engine exceptions whose message misleads the caller with a plain caller error. The
    /// hidden-slide message quotes a 0-based slide number ("Requested slide #1 is hidden") while every
    /// tool parameter is 1-based, and names a load option this server sets itself.
    /// </summary>
    public static Exception Translate(Exception ex, int? slide) => ex switch
    {
        InvalidOperationException when ex.Message.Contains("is hidden", StringComparison.OrdinalIgnoreCase) =>
            new ToolInputException($"slide {slide ?? 1} is hidden. Pass showHidden=true to edit it."),
        _ => ex
    };

    /// <summary>
    /// A plain-words explanation for engine failures whose own message misleads. Returns null when the
    /// engine message is already clear.
    /// </summary>
    public static string? Hint(Exception ex, string family, bool licensed, int? slide = null) => ex switch
    {
        FileLoadException when family == EditorFormats.FixedLayout && !licensed =>
            "Evaluation mode edits only the first 2 PDF pages; request pages 1-2, or set a license.",
        RegexMatchTimeoutException =>
            "The 'find' pattern is too slow on this document; simplify the regular expression.",
        PasswordRequiredException => "Pass the document's password in 'password'.",
        UnauthorizedAccessException => NotWritableHint,
        _ => null
    };

    /// <summary>
    /// The common cause of a denied write is Docker on Linux: the image runs as a non-root user and a
    /// bind mount keeps its host owner, so a folder the user owns is read-only inside the container.
    /// </summary>
    public const string NotWritableHint =
        "The output folder is not writable by the server process. In Docker on Linux the container runs as a " +
        "non-root user, so a host folder you own is read-only to it: add --user \"$(id -u):$(id -g)\" to docker run. " +
        "Otherwise point GROUPDOCS_MCP_OUTPUT_PATH at a writable folder.";

    /// <summary>Output file name in storage: base name of the input plus a suffix and the target extension.</summary>
    public static string OutputName(string inputName, string suffix, string extension) =>
        $"{Path.GetFileNameWithoutExtension(inputName)}{suffix}.{EditorFormats.Normalize(extension)}";

    /// <summary>Validates and normalizes a caller-chosen output name, forcing the target extension.</summary>
    public static string? CallerOutputName(string? outputName, string extension)
    {
        if (string.IsNullOrWhiteSpace(outputName)) return null;
        var name = Path.GetFileName(outputName.Trim());
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ToolInputException($"'outputName' '{outputName}' is not a valid file name.");
        return $"{Path.GetFileNameWithoutExtension(name)}.{EditorFormats.Normalize(extension)}";
    }

    /// <summary>
    /// Stores a file written by the engine and returns Core's "Saved to:" message. <paramref name="rewrite"/>
    /// is only for a name this call reserved itself; everything else gets storage's " (1)" de-duplication.
    /// </summary>
    public static async Task<string> StoreAsync(IFileStorage storage, OutputHelper output, string tempPath, string name, string description,
        bool rewrite = false)
    {
        var bytes = await File.ReadAllBytesAsync(tempPath);
        var saved = await storage.WriteFileAsync(name, bytes, rewrite);
        return await output.BuildFileOutputAsync(saved, description);
    }
}
