using GroupDocs.Editor.Mcp.Tools;
using Xunit;

namespace GroupDocs.Editor.Mcp.Tests;

public class HtmlTextTests
{
    // Shaped like the engine's output: CSS in <head>, text in spans, '&' raw but '<'/'>' escaped.
    private const string Html =
        "<html><head><title>Title of the document</title><style>.Title { color: red } p.Normal{}</style></head>" +
        "<body><p class=\"Normal\"><span class=\"Title\">Title of the document</span></p>" +
        "<p class=\"Normal\"><span>R&D budget &lt;draft&gt;</span></p>" +
        "<p class=\"Normal\"><span>The quick brown fox. Cell 1,1 and Cell 2,1 and Cell 7,1.</span></p>" +
        "<!-- Title of the document --></body></html>";

    private static (string Html, int Count) Run(string find, string replace, bool matchCase = true, bool wholeWord = false, bool regex = false) =>
        HtmlText.Replace(Html, HtmlText.Matcher(find, matchCase, wholeWord, regex), replace, regex);

    [Fact]
    public void Replace_TouchesBodyTextOnly()
    {
        var (html, count) = Run("Title of the document", "Quarterly report");

        Assert.Equal(1, count);
        Assert.Contains("<span class=\"Title\">Quarterly report</span>", html);
        Assert.Contains("<title>Title of the document</title>", html);      // head untouched
        Assert.Contains("<!-- Title of the document -->", html);            // comments untouched
    }

    [Fact]
    public void Replace_NeverEditsCssOrAttributes()
    {
        var (html, count) = Run("Title", "X");
        Assert.Equal(1, count);                                   // only the visible text
        Assert.Contains(".Title { color: red }", html);
        Assert.Contains("class=\"Title\"", html);
    }

    [Fact]
    public void Replace_LiteralMatchesDecodedText_AndReEncodesTheResult()
    {
        var (html, count) = Run("R&D budget <draft>", "Q&A plan <final>");
        Assert.Equal(1, count);
        Assert.Contains("Q&amp;A plan &lt;final&gt;", html);
    }

    [Fact]
    public void Replace_LiteralReplacementIsNotARegexTemplate()
    {
        var (html, _) = Run("quick", "$1 costs $5");
        Assert.Contains("The $1 costs $5 brown fox", html);
    }

    [Fact]
    public void Replace_RegexSupportsGroups()
    {
        var (html, count) = Run(@"Cell (\d),1", "Row $1", regex: true);
        Assert.Equal(3, count);
        Assert.Contains("Row 1 and Row 2 and Row 7", html);
    }

    [Fact]
    public void Replace_CaseInsensitive()
    {
        Assert.Equal(0, Run("TITLE OF THE DOCUMENT", "x").Count);
        Assert.Equal(1, Run("TITLE OF THE DOCUMENT", "x", matchCase: false).Count);
    }

    [Fact]
    public void Replace_WholeWord()
    {
        Assert.Equal(1, Run("row", "x").Count);                   // inside "brown"
        Assert.Equal(0, Run("row", "x", wholeWord: true).Count);
        Assert.Equal(1, Run("fox", "x", wholeWord: true).Count);
    }

    [Fact]
    public void Replace_NoMatch_LeavesHtmlByteIdentical()
    {
        var (html, count) = Run("not in the document", "x");
        Assert.Equal(0, count);
        Assert.Equal(Html, html);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Matcher_EmptyFind_IsACallerError(string? find) =>
        Assert.Throws<ToolInputException>(() => HtmlText.Matcher(find!, true, false, false));

    [Fact]
    public void Matcher_InvalidRegex_IsACallerError() =>
        Assert.Throws<ToolInputException>(() => HtmlText.Matcher("(unclosed", true, false, regex: true));

    [Fact]
    public void VisibleText_IsBodyTextDecodedAndCollapsed()
    {
        var text = HtmlText.VisibleText(Html);
        Assert.StartsWith("Title of the document R&D budget <draft> The quick brown fox.", text);
        Assert.DoesNotContain("color: red", text);
    }

    [Fact]
    public void Replace_StopsWhenTheWholeDocumentBudgetIsSpent()
    {
        // Each node is fast on its own; the budget caps the sum, which the per-match timeout cannot.
        var html = "<html><body>" + string.Concat(Enumerable.Repeat("<p>alpha</p>", 50)) + "</body></html>";
        var matcher = HtmlText.Matcher("alpha", matchCase: true, wholeWord: false, regex: false);

        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() =>
            HtmlText.Replace(html, matcher, "beta", regex: false, budget: TimeSpan.FromTicks(-1)));
    }
}
