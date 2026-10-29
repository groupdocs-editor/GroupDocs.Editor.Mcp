using System.Net;
using System.Text.RegularExpressions;

namespace GroupDocs.Editor.Mcp.Tools;

/// <summary>
/// Text-level operations on the editable HTML GroupDocs.Editor produces.
/// </summary>
/// <remarks>
/// The engine stores text with '&amp;' raw but '&lt;'/'&gt;' escaped ("R&amp;D budget &amp;lt;draft&amp;gt;"), so
/// a regex over the raw markup both misses legitimate matches and can corrupt tags, attributes and
/// CSS. Every operation here works on DECODED text nodes inside &lt;body&gt; only, and re-encodes a node
/// only when it changed — untouched markup stays byte-identical.
/// </remarks>
internal static class HtmlText
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The per-match timeout applies to each text node separately; this caps the whole document, so a
    /// slow pattern cannot run for "nodes × 5 s".
    /// </summary>
    public static readonly TimeSpan ReplaceBudget = TimeSpan.FromSeconds(15);

    // Order matters: skip whole <style>/<script> blocks and comments first, then tags, then text.
    private static readonly Regex Tokens = new(
        @"(?<skip><(?:style|script)\b.*?</(?:style|script)\s*>|<!--.*?-->)|(?<tag><[^>]*>)|(?<text>[^<]+)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BodyOpen = new(@"<body\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BodyClose = new(@"</body\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Builds the matcher for a find request.</summary>
    public static Regex Matcher(string find, bool matchCase, bool wholeWord, bool regex)
    {
        if (string.IsNullOrEmpty(find))
            throw new ToolInputException("'find' must not be empty.");

        var pattern = regex ? find : Regex.Escape(find);
        if (wholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";

        try
        {
            return new Regex(pattern, (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ToolInputException($"'find' is not a valid regular expression: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces matches in the visible text of the body. In regex mode the replacement may use $1-style
    /// group references; in literal mode it is inserted exactly as given. A phrase split across two
    /// formatting runs (two text nodes) is not matched — callers should say so.
    /// </summary>
    public static (string Html, int Count) Replace(string html, Regex matcher, string replacement, bool regex, TimeSpan? budget = null)
    {
        var limit = budget ?? ReplaceBudget;
        var (start, end) = BodyRange(html);
        var count = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var body = Tokens.Replace(html[start..end], m =>
        {
            if (!m.Groups["text"].Success) return m.Value;
            if (clock.Elapsed > limit)
                throw new RegexMatchTimeoutException(matcher.ToString(), "document", limit);

            var decoded = WebUtility.HtmlDecode(m.Value);
            var changed = regex
                ? matcher.Replace(decoded, x => { count++; return x.Result(replacement); })
                : matcher.Replace(decoded, _ => { count++; return replacement; });

            return changed == decoded ? m.Value : Encode(changed);
        });

        return (html[..start] + body + html[end..], count);
    }

    /// <summary>The visible text of the body, whitespace collapsed — for previews.</summary>
    public static string VisibleText(string html)
    {
        var (start, end) = BodyRange(html);
        var parts = Tokens.Matches(html[start..end])
            .Where(m => m.Groups["text"].Success)
            .Select(m => WebUtility.HtmlDecode(m.Value));
        return Regex.Replace(string.Join(" ", parts), @"\s+", " ").Trim();
    }

    /// <summary>Minimal text-node encoding: only what HTML requires, so non-ASCII text stays readable.</summary>
    private static string Encode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace(" ", "&#xa0;");

    private static (int Start, int End) BodyRange(string html)
    {
        var open = BodyOpen.Match(html);
        if (!open.Success) return (0, html.Length);
        var start = open.Index + open.Length;
        var close = BodyClose.Match(html, start);
        return (start, close.Success ? close.Index : html.Length);
    }
}
