using GroupDocs.Editor.Formats;
using GroupDocs.Editor.Formats.Abstraction;
using GroupDocs.Editor.Options;

namespace GroupDocs.Editor.Mcp.Tools;

/// <summary>
/// The format knowledge every tool shares: which family a file belongs to, how to open it, how to
/// edit one part of it, and how to save to a target extension.
/// </summary>
/// <remarks>
/// Verified against GroupDocs.Editor 26.6.1 (see the build-out plan, section 8.3): CSV/TSV are
/// Spreadsheet-family, Markdown is Textual, FixedLayout holds only PDF (XPS is save-only), the engine
/// names two families "Fixed Layout" and "eBook" (mapped here to stable identifiers), worksheet and
/// slide indexes are 0-based on edit and 1-based on save, and PageRange's end is exclusive.
/// </remarks>
internal static class EditorFormats
{
    public const string WordProcessing = "WordProcessing";
    public const string Spreadsheet = "Spreadsheet";
    public const string Presentation = "Presentation";
    public const string FixedLayout = "FixedLayout";
    public const string Textual = "Textual";
    public const string Email = "Email";
    public const string Ebook = "Ebook";

    public static readonly string[] Families =
        [WordProcessing, Spreadsheet, Presentation, FixedLayout, Textual, Email, Ebook];

    /// <summary>Stable family identifier for an engine family (engine names: "Fixed Layout", "eBook").</summary>
    public static string FamilyOf(IDocumentFormat format) => format switch
    {
        WordProcessingFormats => WordProcessing,
        SpreadsheetFormats => Spreadsheet,
        PresentationFormats => Presentation,
        FixedLayoutFormats => FixedLayout,
        TextualFormats => Textual,
        EmailFormats => Email,
        EBookFormats => Ebook,
        _ => format.FormatFamily.Name.Replace(" ", string.Empty) switch
        {
            "eBook" => Ebook,
            var other => other
        }
    };

    public static string Normalize(string extension) => extension.Trim().TrimStart('.').ToLowerInvariant();

    /// <summary>Finds the engine format for an extension across every family, or null.</summary>
    public static DocumentFormatBase? FromExtension(string extension)
    {
        var ext = Normalize(extension);
        if (ext.Length == 0) return null;
        return (DocumentFormatBase?)WordProcessingFormats.FromExtension(ext)
               ?? (DocumentFormatBase?)SpreadsheetFormats.FromExtension(ext)
               ?? (DocumentFormatBase?)PresentationFormats.FromExtension(ext)
               ?? (DocumentFormatBase?)FixedLayoutFormats.FromExtension(ext)
               ?? (DocumentFormatBase?)TextualFormats.FromExtension(ext)
               ?? (DocumentFormatBase?)EBookFormats.FromExtension(ext)
               ?? EmailFormats.FromExtension(ext);
    }

    public static string? FamilyOfExtension(string extension) =>
        FromExtension(extension) is { } f ? FamilyOf(f) : null;

    // ---- catalog ------------------------------------------------------------------------

    /// <summary>Extensions the engine opens, per family (from the engine's own format lists).</summary>
    public static IReadOnlyList<string> LoadFormats(string family) => family switch
    {
        WordProcessing => Extensions(WordProcessingFormats.All),
        Spreadsheet => Extensions(SpreadsheetFormats.All),
        Presentation => Extensions(PresentationFormats.All),
        FixedLayout => Extensions(FixedLayoutFormats.All),
        Textual => Extensions(TextualFormats.All),
        Email => Extensions(EmailFormats.All),
        Ebook => Extensions(EBookFormats.All),
        _ => []
    };

    private static readonly string[] WordTargets = ["docx", "doc", "docm", "dot", "dotx", "dotm", "rtf", "odt", "ott"];
    private static readonly string[] SheetTargets = ["xlsx", "xls", "xlsm", "xlsb", "xltx", "xltm", "xlt", "ods", "csv", "tsv"];
    private static readonly string[] SlideTargets = ["pptx", "ppt", "pptm", "pps", "ppsx", "ppsm", "pot", "potx", "potm", "odp"];
    private static readonly string[] DocumentTargets = ["pdf", "xps", "txt", "md", "mhtml", "epub", "mobi", "azw3"];

    /// <summary>Target extensions a document of this family can be saved to.</summary>
    public static IReadOnlyList<string> SaveFormats(string family) => family switch
    {
        WordProcessing or FixedLayout or Textual or Ebook => [.. WordTargets, .. DocumentTargets],
        Spreadsheet => [.. SheetTargets, "pdf", "xps", "txt", "mhtml"],
        Presentation => [.. SlideTargets, "pdf", "xps"],
        Email => ["eml", "emlx", "msg", "mhtml"],
        _ => []
    };

    private static List<string> Extensions(IEnumerable<IDocumentFormat> formats) =>
        formats.Select(f => Normalize(f.Extension)).Distinct().OrderBy(e => e).ToList();

    // ---- open ---------------------------------------------------------------------------

    /// <summary>
    /// Opens a document with family-specific load options. Word documents ALWAYS get explicit
    /// WordProcessingLoadOptions: without them the engine's FormFieldManager throws
    /// NullReferenceException (measured on 26.6.1).
    /// </summary>
    public static Editor Open(string path, string? password)
    {
        ILoadOptions? options = FamilyOfExtension(Path.GetExtension(path)) switch
        {
            WordProcessing => new WordProcessingLoadOptions { Password = password },
            Spreadsheet when !IsDelimited(path) => new SpreadsheetLoadOptions { Password = password },
            Presentation => new PresentationLoadOptions { Password = password },
            FixedLayout => new PdfLoadOptions { Password = password },
            _ => null
        };
        return options is null ? new Editor(path) : new Editor(path, options);
    }

    public static bool IsDelimited(string pathOrExtension)
    {
        var ext = Normalize(Path.GetExtension(pathOrExtension) is { Length: > 0 } e ? e : pathOrExtension);
        return ext is "csv" or "tsv" or "dsv";
    }

    // ---- edit ---------------------------------------------------------------------------

    public sealed record Part(string Kind, int Number, int Of)
    {
        public override string ToString() => $"{Kind} {Number} of {Of}";
    }

    /// <summary>
    /// Evaluation mode reaches only the first 2 worksheets of a workbook and the first 2 slides of a
    /// presentation, and a higher number silently returns part 2 instead (measured on 26.6.1: slide 3
    /// came back byte-identical to slide 2). Saving into part 3+ is not capped.
    /// </summary>
    public const int EvaluationPartLimit = 2;

    /// <summary>
    /// Builds edit options for one part of a document. <paramref name="worksheet"/> and
    /// <paramref name="slide"/> are 1-based and count every part, hidden ones included — the same
    /// numbering the save side uses, so an edited part splices back where it came from. Spreadsheets
    /// and presentations always resolve to a concrete part (default 1).
    /// </summary>
    public static (IEditOptions? Options, Part? Part) EditOptions(
        string family, string path, int partCount,
        int? worksheet, int? slide, string? pages, bool? pagination, bool? skipImages, bool showHidden, bool licensed)
    {
        if (worksheet is not null && family != Spreadsheet)
            throw new ToolInputException("'worksheet' applies only to spreadsheets (XLSX, XLS, ODS, …).");
        if (slide is not null && family != Presentation)
            throw new ToolInputException("'slide' applies only to presentations (PPTX, PPT, ODP, …).");
        if (pages is not null && family != FixedLayout)
            throw new ToolInputException("'pages' applies only to PDF documents.");

        switch (family)
        {
            case Spreadsheet when IsDelimited(path):
                return (new DelimitedTextEditOptions(Normalize(Path.GetExtension(path)) == "tsv" ? "\t" : ","), null);

            case Spreadsheet:
            {
                var n = CheckPart("worksheet", worksheet ?? 1, partCount);
                return (SheetEditOptions(path, n, licensed), new Part("worksheet", n, partCount));
            }

            case Presentation:
            {
                var n = CheckPart("slide", slide ?? 1, partCount);
                CheckEvaluationReach("slide", "slides of a presentation", n, licensed);
                return (new PresentationEditOptions { SlideNumber = n - 1, ShowHiddenSlides = showHidden },
                    new Part("slide", n, partCount));
            }

            case FixedLayout:
            {
                var o = new PdfEditOptions { EnablePagination = pagination ?? false, SkipImages = skipImages ?? false };
                if (pages is not null) o.Pages = ParsePages(pages, partCount);
                return (o, null);
            }

            case WordProcessing:
                return (new WordProcessingEditOptions { EnablePagination = pagination ?? false }, null);

            case Ebook:
                return (new EbookEditOptions { EnablePagination = pagination ?? false }, null);

            case Email:
                return (new EmailEditOptions(MailMessageOutput.All), null);

            case Textual:
                return (Normalize(Path.GetExtension(path)) switch
                {
                    "txt" => new TextEditOptions { EnablePagination = pagination ?? false },
                    "md" => new MarkdownEditOptions(),
                    "xml" => new XmlEditOptions(),
                    _ => null
                }, null);

            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Edit options for worksheet <paramref name="n"/> (1-based, hidden sheets counted). The engine's
    /// WorksheetIndex misbehaves around hidden sheets (measured on 26.6.1): with hidden sheets excluded it
    /// counts visible sheets only, and with them included a hidden sheet comes back as worksheet 1. So
    /// when the workbook's sheet states can be read, hidden sheets are excluded and the absolute number is
    /// mapped to its visible position; a hidden worksheet itself is refused rather than edited wrongly.
    /// </summary>
    private static SpreadsheetEditOptions SheetEditOptions(string path, int n, bool licensed)
    {
        var hidden = HiddenWorksheets(path);
        if (hidden is null)
        {
            CheckEvaluationReach("worksheet", "worksheets of a workbook", n, licensed);
            return new SpreadsheetEditOptions { WorksheetIndex = n - 1, ExcludeHiddenWorksheets = false };
        }

        if (n <= hidden.Count && hidden[n - 1])
            throw new ToolInputException(
                $"worksheet {n} is hidden, and GroupDocs.Editor does not edit hidden worksheets reliably (it returns a " +
                "different worksheet). Unhide it in the workbook first.");

        var visibleIndex = hidden.Take(n - 1).Count(h => !h);
        if (!licensed && visibleIndex >= EvaluationPartLimit)
            throw new ToolInputException(
                $"Evaluation mode edits only the first {EvaluationPartLimit} visible worksheets of a workbook; worksheet {n} " +
                "is beyond them. Set GROUPDOCS_LICENSE_PATH, or the metered keys, to edit every worksheet.");
        return new SpreadsheetEditOptions { WorksheetIndex = visibleIndex, ExcludeHiddenWorksheets = true };
    }

    private static void CheckEvaluationReach(string kind, string what, int n, bool licensed)
    {
        if (!licensed && n > EvaluationPartLimit)
            throw new ToolInputException(
                $"Evaluation mode edits only the first {EvaluationPartLimit} {what}; {kind} {n} is beyond them. " +
                "Set GROUPDOCS_LICENSE_PATH, or the metered keys, to edit every part.");
    }

    /// <summary>
    /// Hidden flag of every worksheet, in order, read from an Office Open XML workbook's
    /// xl/workbook.xml (state="hidden" or "veryHidden"). Null for other formats, encrypted
    /// workbooks, or anything unreadable.
    /// </summary>
    public static IReadOnlyList<bool>? HiddenWorksheets(string path)
    {
        if (Normalize(Path.GetExtension(path)) is not ("xlsx" or "xlsm" or "xltx" or "xltm")) return null;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = zip.GetEntry("xl/workbook.xml");
            if (entry is null) return null;
            using var stream = entry.Open();
            var doc = System.Xml.Linq.XDocument.Load(stream);
            return doc.Descendants()
                .Where(e => e.Name.LocalName == "sheet" && e.Parent?.Name.LocalName == "sheets")
                .Select(e => (string?)e.Attribute("state") is "hidden" or "veryHidden")
                .ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            return null;    // encrypted workbooks are OLE containers, not zip archives
        }
    }

    internal static int CheckPart(string kind, int number, int count)
    {
        if (number < 1)
            throw new ToolInputException($"'{kind}' is 1-based; {number} is not a valid {kind} number.");
        if (count > 0 && number > count)
            throw new ToolInputException($"There is no {kind} {number}; the document has {count} {kind}(s).");
        return number;
    }

    /// <summary>"3" or "2-4" (1-based, inclusive) → PageRange. The engine's end bound is exclusive, so a count is used.</summary>
    public static PageRange ParsePages(string pages, int pageCount)
    {
        var m = System.Text.RegularExpressions.Regex.Match(pages.Trim(), @"^(\d+)\s*(?:-\s*(\d+))?$");
        if (!m.Success)
            throw new ToolInputException($"'pages' must look like \"3\" or \"2-4\" (1-based, inclusive); got '{pages}'.");

        var start = int.Parse(m.Groups[1].Value);
        var end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start;
        if (start < 1 || end < start)
            throw new ToolInputException($"'pages' range '{pages}' is invalid: pages are 1-based and the end must not precede the start.");
        if (pageCount > 0 && start > pageCount)
            throw new ToolInputException($"There is no page {start}; the document has {pageCount} page(s).");
        if (pageCount > 0) end = Math.Min(end, pageCount);

        return PageRange.FromStartPageWithCount((ushort)start, (ushort)(end - start + 1));
    }

    // ---- save ---------------------------------------------------------------------------

    /// <summary>
    /// Save options for a target extension. <paramref name="sheetOrSlide"/> splices the edited part
    /// back into the source document at that 1-based position (the engine's save options are 1-based).
    /// </summary>
    public static ISaveOptions SaveOptions(string targetExtension, string? password, int? worksheet = null, int? slide = null)
    {
        var ext = Normalize(targetExtension);
        var format = FromExtension(ext);

        ISaveOptions options = format switch
        {
            WordProcessingFormats w => new WordProcessingSaveOptions(w) { Password = password },
            SpreadsheetFormats when IsDelimited(ext) => new DelimitedTextSaveOptions(ext == "tsv" ? "\t" : ","),
            SpreadsheetFormats s => Sheet(s),
            PresentationFormats p => Slides(p),
            FixedLayoutFormats => new PdfSaveOptions { Password = password },
            TextualFormats t when Normalize(t.Extension) == "md" => new MarkdownSaveOptions { ExportImagesAsBase64 = true },
            TextualFormats t when Normalize(t.Extension) == "mhtml" => new MhtmlSaveOptions(),
            TextualFormats t when Normalize(t.Extension) == "txt" => new TextSaveOptions(),
            EBookFormats e => new EbookSaveOptions(e),
            EmailFormats => new EmailSaveOptions(MailMessageOutput.All),
            null when ext == "xps" => new XpsSaveOptions(),
            _ => throw new ToolInputException(
                $"GroupDocs.Editor cannot save to '{targetExtension}'. Supported targets: " +
                string.Join(", ", AllSaveTargets()) + ".")
        };

        if (password is not null && options is not (WordProcessingSaveOptions or SpreadsheetSaveOptions or PresentationSaveOptions or PdfSaveOptions))
            throw new ToolInputException(
                $"Password protection is not available for '{ext}' output; it is supported for Word, spreadsheet, presentation and PDF targets.");

        return options;

        SpreadsheetSaveOptions Sheet(SpreadsheetFormats s)
        {
            var o = new SpreadsheetSaveOptions(s) { Password = password };
            if (worksheet is { } n) { o.WorksheetNumber = n; o.InsertAsNewWorksheet = false; }
            return o;
        }

        PresentationSaveOptions Slides(PresentationFormats p)
        {
            var o = new PresentationSaveOptions(p) { Password = password };
            if (slide is { } n) { o.SlideNumber = n; o.InsertAsNewSlide = false; }
            return o;
        }
    }

    /// <summary>Whether output in this format can carry a password (Word, workbook, presentation, PDF).</summary>
    public static bool SupportsPassword(string targetExtension) =>
        FromExtension(targetExtension) is WordProcessingFormats or PresentationFormats or FixedLayoutFormats
        || (FromExtension(targetExtension) is SpreadsheetFormats && !IsDelimited(targetExtension));

    public static IReadOnlyList<string> AllSaveTargets() =>
        Families.SelectMany(SaveFormats).Distinct().OrderBy(e => e).ToList();

    /// <summary>Targets the engine can create from scratch (no original document). PPTX and email need one.</summary>
    public static bool CanCreateBlank(string targetExtension) =>
        FromExtension(targetExtension) is WordProcessingFormats or SpreadsheetFormats or FixedLayoutFormats or TextualFormats or EBookFormats
        || Normalize(targetExtension) == "xps";

    /// <summary>The engine format to create a blank document of, for a target extension.</summary>
    public static DocumentFormatBase BlankFormatFor(string targetExtension) =>
        Normalize(targetExtension) == "xps"
            ? WordProcessingFormats.Docx
            : FromExtension(targetExtension) switch
            {
                SpreadsheetFormats s when IsDelimited(targetExtension) => SpreadsheetFormats.Xlsx,
                { } f => f,
                null => WordProcessingFormats.Docx
            };
}
