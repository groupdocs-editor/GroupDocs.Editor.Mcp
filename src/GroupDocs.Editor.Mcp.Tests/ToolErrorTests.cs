using GroupDocs.Editor.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GroupDocs.Editor.Mcp.Tests;

public class ToolErrorTests
{
    private static string Text(CallToolResult r) => Assert.IsType<TextContentBlock>(Assert.Single(r.Content)).Text;

    [Fact]
    public void Failure_SetsIsError_AndStartsWithThePrefix()
    {
        var result = ToolError.Failure("HTML extraction", "report.docx",
            new InvalidOperationException("boom", new IOException("disk")));

        Assert.True(result.IsError);
        var text = Text(result);
        Assert.StartsWith("HTML extraction failed for 'report.docx': System.InvalidOperationException: boom", text);
        Assert.Contains("inner(0): System.IO.IOException: disk", text);
    }

    [Fact]
    public void Failure_CallerErrors_ArePlainWords()
    {
        var text = Text(ToolError.Failure("Text replacement", "a.docx", new ToolInputException("'find' must not be empty.")));
        Assert.Equal("Text replacement failed for 'a.docx': 'find' must not be empty.", text);
    }

    [Fact]
    public void Failure_AppendsHintAndSubject()
    {
        var text = Text(ToolError.Failure("Save from HTML", "a.html", new ToolInputException("nope"), subject: "(-> xyz)", hint: "Try docx."));
        Assert.Equal("Save from HTML failed for 'a.html' (-> xyz): nope Try docx.", text);
    }

    [Fact]
    public void Hint_DeniedWrite_NamesTheDockerUserFix()
    {
        // What a write into /data returns in Docker on Linux when the mount is owned by the host user.
        var denied = new UnauthorizedAccessException("Access to the path '/data/sample.html' is denied.");

        var text = Text(ToolError.Failure("HTML extraction", "sample.docx", denied,
            hint: EditorTool.Hint(denied, EditorFormats.WordProcessing, licensed: false)));

        Assert.StartsWith("HTML extraction failed for 'sample.docx': System.UnauthorizedAccessException", text);
        Assert.Contains("--user \"$(id -u):$(id -g)\"", text);
        Assert.Contains("GROUPDOCS_MCP_OUTPUT_PATH", text);
    }

    [Fact]
    public void Translate_HiddenSlide_ReportsTheOneBasedNumberOnly()
    {
        var engine = new InvalidOperationException(
            "Requested slide #1 is hidden. In order to edit it please set the 'ShowHiddenSlides' flag in 'PresentationLoadOptions' to 'true'");

        var text = Text(ToolError.Failure("HTML extraction", "deck.pptx", EditorTool.Translate(engine, slide: 2)));

        Assert.Equal("HTML extraction failed for 'deck.pptx': slide 2 is hidden. Pass showHidden=true to edit it.", text);
    }

    [Fact]
    public void Translate_OtherExceptions_PassThrough()
    {
        var ex = new IOException("x");
        Assert.Same(ex, EditorTool.Translate(ex, 1));
    }

    [Fact]
    public void Success_DoesNotSetIsError() =>
        Assert.NotEqual(true, ToolError.Success("ok").IsError);

    [Fact]
    public void Scrub_ReplacesWorkspacePaths_WithTheFileName()
    {
        using var work = new TempWorkspace();
        var leaked = $"The parameter is incorrect. : '{work.PathFor("report.docx")}'.";

        var scrubbed = TempWorkspace.Scrub(leaked, "report.docx");

        Assert.Equal("The parameter is incorrect. : 'report.docx'.", scrubbed);
        Assert.DoesNotContain(Path.GetTempPath(), scrubbed);
    }

    [Fact]
    public void Scrub_NamesTheFileThePathPointsAt_NotAlwaysTheMainInput()
    {
        // save_from_html works on two inputs; an engine error about the source must name the source.
        using var work = new TempWorkspace();
        var leaked = $"Cannot open '{work.PathFor("source_deck.pptx")}' while saving '{work.PathFor("out_new.pptx")}' (in '{work.Root}')";

        var scrubbed = TempWorkspace.Scrub(leaked, "edited.html");

        Assert.Equal("Cannot open 'deck.pptx' while saving 'new.pptx' (in 'edited.html')", scrubbed);
    }

    [Fact]
    public void Hint_RegexTimeout_AsksForASimplerPattern()
    {
        var ex = new System.Text.RegularExpressions.RegexMatchTimeoutException("(a+)+$", "document", TimeSpan.FromSeconds(15));
        Assert.Contains("simplify the regular expression", EditorTool.Hint(ex, EditorFormats.WordProcessing, licensed: true));
    }

    [Fact]
    public void Workspace_IsDeletedOnDispose()
    {
        string root;
        using (var work = new TempWorkspace())
        {
            root = work.Root;
            File.WriteAllText(work.PathFor("x.txt"), "x");
        }
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("out.docx", "docx", "out.docx")]
    [InlineData("out", "pdf", "out.pdf")]
    [InlineData("sub/out.txt", "pdf", "out.pdf")]   // no directories: outputs land in storage root
    [InlineData(null, "pdf", null)]
    public void CallerOutputName_ForcesTheTargetExtension(string? name, string ext, string? expected) =>
        Assert.Equal(expected, EditorTool.CallerOutputName(name, ext));

    [Fact]
    public void OutputName_UsesBaseNameSuffixAndExtension() =>
        Assert.Equal("contract_edited.pdf", EditorTool.OutputName("contract.docx", "_edited", ".PDF"));
}
