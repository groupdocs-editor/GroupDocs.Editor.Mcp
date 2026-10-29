using GroupDocs.Editor.Mcp.Tools;
using GroupDocs.Editor.Options;
using Xunit;

namespace GroupDocs.Editor.Mcp.Tests;

public class EditorFormatsTests
{
    [Theory]
    [InlineData("report.docx", "WordProcessing")]
    [InlineData(".DOCX", "WordProcessing")]
    [InlineData("budget.xlsx", "Spreadsheet")]
    [InlineData("data.csv", "Spreadsheet")]      // the engine files CSV under Spreadsheet, not Textual
    [InlineData("deck.pptx", "Presentation")]
    [InlineData("scan.pdf", "FixedLayout")]      // engine name "Fixed Layout"
    [InlineData("notes.md", "Textual")]          // no Markdown family in the engine
    [InlineData("notes.txt", "Textual")]
    [InlineData("book.epub", "Ebook")]           // engine name "eBook"
    [InlineData("mail.eml", "Email")]
    public void FamilyOfExtension_MapsToStableIdentifiers(string file, string family) =>
        Assert.Equal(family, EditorFormats.FamilyOfExtension(Path.GetExtension(file) is { Length: > 0 } e ? e : file));

    [Fact]
    public void FamilyOfExtension_Unknown_IsNull() =>
        Assert.Null(EditorFormats.FamilyOfExtension(".xyz"));

    [Fact]
    public void LoadAndSaveFormats_CoverEveryFamily()
    {
        foreach (var family in EditorFormats.Families)
        {
            Assert.NotEmpty(EditorFormats.LoadFormats(family));
            Assert.NotEmpty(EditorFormats.SaveFormats(family));
            Assert.All(EditorFormats.LoadFormats(family), e => Assert.DoesNotContain(".", e));
        }
        Assert.Contains("docx", EditorFormats.SaveFormats(EditorFormats.WordProcessing));
        Assert.Contains("pdf", EditorFormats.SaveFormats(EditorFormats.WordProcessing));
        Assert.Contains("csv", EditorFormats.LoadFormats(EditorFormats.Spreadsheet));
        Assert.Contains("md", EditorFormats.LoadFormats(EditorFormats.Textual));
    }

    // ---- part selection: callers are 1-based, the engine is 0-based ------------------------

    [Fact]
    public void EditOptions_Worksheet_IsConvertedToZeroBased()
    {
        var (options, part) = EditorFormats.EditOptions("Spreadsheet", "a.xlsx", 3, worksheet: 2, slide: null, pages: null, pagination: null, skipImages: null, showHidden: false, licensed: true);

        var o = Assert.IsType<SpreadsheetEditOptions>(options);
        Assert.Equal(1, o.WorksheetIndex);
        Assert.False(o.ExcludeHiddenWorksheets);   // no readable sheet states: absolute numbering
        Assert.Equal("worksheet 2 of 3", part!.ToString());
    }

    [Fact]
    public void EditOptions_Spreadsheet_DefaultsToFirstWorksheet()
    {
        var (options, part) = EditorFormats.EditOptions("Spreadsheet", "a.xlsx", 2, null, null, null, null, null, false, licensed: true);
        Assert.Equal(0, Assert.IsType<SpreadsheetEditOptions>(options).WorksheetIndex);
        Assert.Equal(1, part!.Number);
    }

    [Fact]
    public void EditOptions_Slide_IsConvertedToZeroBased_AndShowHiddenPassesThrough()
    {
        var (options, part) = EditorFormats.EditOptions("Presentation", "a.pptx", 3, null, slide: 3, null, null, null, showHidden: true, licensed: true);
        var o = Assert.IsType<PresentationEditOptions>(options);
        Assert.Equal(2, o.SlideNumber);
        Assert.True(o.ShowHiddenSlides);
        Assert.Equal("slide 3 of 3", part!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void EditOptions_SlideOutOfRange_IsACallerError(int slide) =>
        Assert.Throws<ToolInputException>(() =>
            EditorFormats.EditOptions("Presentation", "a.pptx", 3, null, slide, null, null, null, false, licensed: true));

    [Fact]
    public void EditOptions_WorksheetOnAWordDocument_IsACallerError()
    {
        var ex = Assert.Throws<ToolInputException>(() =>
            EditorFormats.EditOptions("WordProcessing", "a.docx", 3, worksheet: 1, null, null, null, null, false, licensed: true));
        Assert.Contains("spreadsheets", ex.Message);
    }

    [Fact]
    public void EditOptions_Csv_UsesDelimitedTextOptions() =>
        Assert.IsType<DelimitedTextEditOptions>(
            EditorFormats.EditOptions("Spreadsheet", "cars.csv", 1, null, null, null, null, null, false, licensed: true).Options);

    // ---- evaluation mode reaches only parts 1-2 (measured: slide 3 came back as slide 2) ------

    [Fact]
    public void EditOptions_Unlicensed_SlideBeyondTwo_IsRefused_NotClamped()
    {
        var ex = Assert.Throws<ToolInputException>(() =>
            EditorFormats.EditOptions("Presentation", "a.pptx", 3, null, slide: 3, null, null, null, false, licensed: false));
        Assert.Contains("Evaluation mode edits only the first 2", ex.Message);
    }

    [Fact]
    public void EditOptions_Unlicensed_WorksheetBeyondTwo_IsRefused_NotClamped() =>
        Assert.Throws<ToolInputException>(() =>
            EditorFormats.EditOptions("Spreadsheet", "a.xlsx", 3, worksheet: 3, null, null, null, null, false, licensed: false));

    [Fact]
    public void EditOptions_Unlicensed_PartTwo_IsAllowed() =>
        Assert.Equal(1, Assert.IsType<PresentationEditOptions>(
            EditorFormats.EditOptions("Presentation", "a.pptx", 3, null, slide: 2, null, null, null, true, licensed: false).Options).SlideNumber);

    // ---- hidden worksheets: numbering counts every sheet, as the save side does --------------

    [Fact]
    public void EditOptions_HiddenSheetBefore_MapsTheAbsoluteNumberToItsVisiblePosition()
    {
        var path = HiddenMiddleSheetWorkbook();
        Assert.Equal([false, true, false], EditorFormats.HiddenWorksheets(path));

        var o = Assert.IsType<SpreadsheetEditOptions>(
            EditorFormats.EditOptions("Spreadsheet", path, 3, worksheet: 3, null, null, null, null, false, licensed: false).Options);

        // Sheet 3 is the 2nd visible sheet: within the evaluation reach, and index 1 once hidden ones are excluded.
        Assert.True(o.ExcludeHiddenWorksheets);
        Assert.Equal(1, o.WorksheetIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditOptions_HiddenWorksheet_IsRefused_EvenWithShowHidden(bool showHidden)
    {
        var ex = Assert.Throws<ToolInputException>(() =>
            EditorFormats.EditOptions("Spreadsheet", HiddenMiddleSheetWorkbook(), 3, worksheet: 2, null, null, null, null, showHidden, licensed: true));
        Assert.Contains("worksheet 2 is hidden", ex.Message);
    }

    [Fact]
    public void HiddenWorksheets_IsNullForNonZipWorkbooks() =>
        Assert.Null(EditorFormats.HiddenWorksheets("missing.xls"));

    [Theory]
    [InlineData("docx", true)]
    [InlineData("xlsx", true)]
    [InlineData("pptx", true)]
    [InlineData("pdf", true)]
    [InlineData("csv", false)]
    [InlineData("txt", false)]
    [InlineData("md", false)]
    public void SupportsPassword(string target, bool expected) =>
        Assert.Equal(expected, EditorFormats.SupportsPassword(target));

    /// A minimal workbook: Alpha | Bravo (hidden) | Charlie — only xl/workbook.xml matters here.
    private static string HiddenMiddleSheetWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hidden-{Guid.NewGuid():N}.xlsx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open()))
        {
            writer.Write("""
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Alpha" sheetId="1" r:id="rId1"/>
                    <sheet name="Bravo" sheetId="2" state="hidden" r:id="rId2"/>
                    <sheet name="Charlie" sheetId="3" r:id="rId3"/>
                  </sheets>
                </workbook>
                """);
        }
        return path;
    }

    // ---- PDF pages: the engine's PageRange end is exclusive ------------------------------

    [Theory]
    [InlineData("1", 1, 1)]
    [InlineData("1-1", 1, 1)]
    [InlineData("2-4", 2, 3)]
    [InlineData(" 3 - 5 ", 3, 3)]
    [InlineData("5-99", 5, 2)]   // clipped to the 6-page document
    public void ParsePages_IsInclusiveAndOneBased(string pages, int start, int count)
    {
        var range = EditorFormats.ParsePages(pages, pageCount: 6);
        Assert.Equal(start, range.StartNumber);
        Assert.Equal(count, range.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4-2")]
    [InlineData("a-b")]
    [InlineData("7")]
    public void ParsePages_Invalid_IsACallerError(string pages) =>
        Assert.Throws<ToolInputException>(() => EditorFormats.ParsePages(pages, pageCount: 6));

    // ---- save options --------------------------------------------------------------------

    [Fact]
    public void SaveOptions_WordWithPassword()
    {
        var o = Assert.IsType<WordProcessingSaveOptions>(EditorFormats.SaveOptions("docx", "pw"));
        Assert.Equal("pw", o.Password);
    }

    [Fact]
    public void SaveOptions_SpliceWorksheet_IsOneBasedAndReplaces()
    {
        var o = Assert.IsType<SpreadsheetSaveOptions>(EditorFormats.SaveOptions("xlsx", null, worksheet: 2));
        Assert.Equal(2, o.WorksheetNumber);
        Assert.False(o.InsertAsNewWorksheet);
    }

    [Fact]
    public void SaveOptions_SpliceSlide_IsOneBasedAndReplaces()
    {
        var o = Assert.IsType<PresentationSaveOptions>(EditorFormats.SaveOptions("pptx", null, slide: 1));
        Assert.Equal(1, o.SlideNumber);
        Assert.False(o.InsertAsNewSlide);
    }

    [Theory]
    [InlineData("pdf", typeof(PdfSaveOptions))]
    [InlineData("xps", typeof(XpsSaveOptions))]
    [InlineData("txt", typeof(TextSaveOptions))]
    [InlineData("md", typeof(MarkdownSaveOptions))]
    [InlineData("csv", typeof(DelimitedTextSaveOptions))]
    [InlineData("epub", typeof(EbookSaveOptions))]
    [InlineData(".ODT", typeof(WordProcessingSaveOptions))]
    public void SaveOptions_MapsTargets(string ext, Type expected) =>
        Assert.IsType(expected, EditorFormats.SaveOptions(ext, null));

    [Fact]
    public void SaveOptions_UnknownTarget_NamesTheSupportedOnes()
    {
        var ex = Assert.Throws<ToolInputException>(() => EditorFormats.SaveOptions("not-a-format", null));
        Assert.Contains("not-a-format", ex.Message);
        Assert.Contains("docx", ex.Message);
    }

    [Fact]
    public void SaveOptions_PasswordOnTxt_IsACallerError() =>
        Assert.Throws<ToolInputException>(() => EditorFormats.SaveOptions("txt", "pw"));

    [Theory]
    [InlineData("docx", true)]
    [InlineData("pdf", true)]
    [InlineData("md", true)]
    [InlineData("xps", true)]
    [InlineData("pptx", false)]   // engine: "Please attach original stylesheet"
    [InlineData("eml", false)]    // engine: "invalid HTML document"
    public void CanCreateBlank_MatchesMeasuredEngineBehaviour(string ext, bool expected) =>
        Assert.Equal(expected, EditorFormats.CanCreateBlank(ext));
}
