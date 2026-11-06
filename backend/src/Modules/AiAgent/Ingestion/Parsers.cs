using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Nihasdesk.Modules.AiAgent.Domain;
using SmartReader;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

internal sealed record DocumentBlock(SectionKind Kind, string Text);

/// <summary>Blocks under one heading path; a new one starts at each heading and each PDF page.</summary>
internal sealed record DocumentSection(IReadOnlyList<string> Headings, int? Page, IReadOnlyList<DocumentBlock> Blocks);

/// <summary>What every parser returns (ADR 0026 §2), whatever the file was.</summary>
internal sealed record ParsedDocument(string? Title, string? Language, IReadOnlyList<DocumentSection> Sections, int? Pages, IReadOnlyList<int> PagesWithoutText);

/// <summary>The source can't be read and trying again won't help. <see cref="Code"/> becomes the failure reason.</summary>
internal sealed class SourceReadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static class FailureCodes
{
    public const string Scanned = "scanned";
    public const string Encrypted = "encrypted";
    public const string TooManyPages = "too_many_pages";
    public const string Unreadable = "unreadable";
    public const string NoText = "no_text";
    public const string TooComplex = "too_complex";
    public const string Duplicate = "duplicate";
    public const string NotAPage = "not_a_page";
    public const string BlockedUrl = "blocked_url";
    public const string InvalidUrl = "invalid_url";
    public const string PageTooLarge = "page_too_large";
    public const string PageUnavailable = "page_unavailable";
    public const string EmbeddingFailed = "embedding_failed";
    public const string ReadFailed = "read_failed";
}

/// <summary>Reads one kind of source. Imported text is data, never instructions (SEC-35).</summary>
internal interface IDocumentParser
{
    ParsedDocument Parse(byte[] content, string origin);
}

/// <summary>Collects blocks into sections while a parser walks a document in reading order.</summary>
internal sealed class SectionBuilder
{
    private readonly List<DocumentSection> _sections = [];
    private readonly List<string> _headings = [];
    private readonly List<int> _levels = [];
    private List<DocumentBlock> _blocks = [];
    private int? _page;

    public IReadOnlyList<DocumentSection> Sections
    {
        get
        {
            Flush();
            return _sections;
        }
    }

    public void Heading(int level, string text)
    {
        Flush();
        while (_levels.Count > 0 && _levels[^1] >= level)
        {
            _levels.RemoveAt(_levels.Count - 1);
            _headings.RemoveAt(_headings.Count - 1);
        }

        _levels.Add(level);
        _headings.Add(text);
    }

    public void Page(int page)
    {
        Flush();
        _page = page;
    }

    public void Add(SectionKind kind, string text)
    {
        var clean = text.Trim();
        if (clean.Length > 0)
        {
            _blocks.Add(new DocumentBlock(kind, clean));
        }
    }

    private void Flush()
    {
        if (_blocks.Count > 0)
        {
            _sections.Add(new DocumentSection([.. _headings], _page, _blocks));
            _blocks = [];
        }
    }
}

/// <summary>
/// PDF with selectable text (PdfPig): headings by font size; tables by the wide gaps between their
/// columns, as a PDF has no table structure. Pages without text are reported; scans can't be read
/// (no OCR, PRD 4.7).
/// </summary>
internal sealed partial class PdfParser : IDocumentParser
{
    public const int MaxPages = 300;
    private const double HeadingRatio = 1.15;
    private const int HeadingMaxLength = 120;
    private const double ParagraphGapRatio = 1.3;
    private const int MinPagesForRepeats = 3;
    private const double MarginShare = 0.12;
    private const double ColumnGapRatio = 1.5;
    private const string ColumnSeparator = " | ";

    private sealed record Line(int Page, string Text, double Size, double Baseline, double PageHeight);

    public ParsedDocument Parse(byte[] content, string origin)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(content);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new SourceReadException(FailureCodes.Encrypted);
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            throw new SourceReadException(FailureCodes.Unreadable);
        }

        using (document)
        {
            if (document.NumberOfPages > MaxPages)
            {
                throw new SourceReadException(FailureCodes.TooManyPages);
            }

            var all = document.GetPages().SelectMany(LinesOf).ToList();
            var lines = WithoutRepeatedLines(all, document.NumberOfPages);
            if (lines.Count == 0)
            {
                throw new SourceReadException(FailureCodes.Scanned);
            }

            var withText = lines.Select(l => l.Page).ToHashSet();
            var empty = Enumerable.Range(1, document.NumberOfPages).Where(p => !withText.Contains(p)).ToList();
            return new ParsedDocument(document.Information.Title, null, Build(lines), document.NumberOfPages, empty);
        }
    }

    private static List<Line> LinesOf(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Letters.Count > 0).ToList();
        var lines = new List<Line>();
        // By baseline, so letters that hang below it ("g", "p") stay on their line.
        foreach (var row in words.GroupBy(w => Math.Round(w.Letters[0].StartBaseLine.Y)).OrderByDescending(g => g.Key))
        {
            var ordered = row.OrderBy(w => w.BoundingBox.Left).ToList();
            lines.Add(new Line(
                page.Number,
                Join(ordered),
                ordered.Max(w => w.Letters.Max(l => l.PointSize)),
                row.Key,
                page.Height));
        }

        return lines;
    }

    /// <summary>A gap much wider than a space is a new column.</summary>
    private static string Join(List<Word> words)
    {
        var text = new StringBuilder(words[0].Text);
        for (var i = 1; i < words.Count; i++)
        {
            var size = words[i].Letters.Max(l => l.PointSize);
            var gap = words[i].BoundingBox.Left - words[i - 1].BoundingBox.Right;
            text.Append(gap > size * ColumnGapRatio ? ColumnSeparator : " ").Append(words[i].Text);
        }

        return text.ToString();
    }

    /// <summary>
    /// Running headers and footers: the same text (page numbers aside) near the top or bottom of
    /// most pages. They say nothing about the content and would land inside sections.
    /// </summary>
    private static List<Line> WithoutRepeatedLines(List<Line> lines, int pages)
    {
        if (pages < MinPagesForRepeats)
        {
            return lines;
        }

        static string Key(Line line) => Digits().Replace(line.Text, "#").ToUpperInvariant();
        static bool InMargin(Line line) =>
            line.Baseline > line.PageHeight * (1 - MarginShare) || line.Baseline < line.PageHeight * MarginShare;

        var repeated = lines
            .Where(InMargin)
            .GroupBy(Key)
            .Where(g => g.Select(l => l.Page).Distinct().Count() >= Math.Max(MinPagesForRepeats, pages / 2))
            .Select(g => g.Key)
            .ToHashSet();
        return lines.Where(l => !(InMargin(l) && repeated.Contains(Key(l)))).ToList();
    }

    /// <summary>The usual distance between lines of a paragraph; a clearly larger one starts a new paragraph.</summary>
    private static double LineSpacing(List<Line> lines, double body)
    {
        var gaps = lines
            .Zip(lines.Skip(1))
            .Where(p => p.First.Page == p.Second.Page && !IsHeading(p.First, body) && !IsHeading(p.Second, body))
            .Select(p => p.First.Baseline - p.Second.Baseline)
            .Where(g => g > 0)
            .Order()
            .ToList();
        // Low in the sorted list: paragraph breaks are the larger gaps, and in a short document they can be half.
        return gaps.Count == 0 ? body * 1.6 : gaps[gaps.Count / 3];
    }

    private static List<DocumentSection> Build(List<Line> lines)
    {
        // The most common size by amount of text is the body; clearly larger short lines are headings.
        var body = lines.GroupBy(l => Math.Round(l.Size, 1)).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
        var headingSizes = lines
            .Where(l => IsHeading(l, body))
            .Select(l => Math.Round(l.Size, 1))
            .Distinct()
            .OrderDescending()
            .ToList();
        var newParagraphGap = LineSpacing(lines, body) * ParagraphGapRatio;

        var builder = new SectionBuilder();
        var paragraph = new StringBuilder();
        var rows = new List<string>();
        var page = 0;
        int? nextPage = null;
        var lastBlock = string.Empty;
        Line? previous = null;

        // A new page starts a new section with its number, but not between a question and its answer.
        void SwitchPage()
        {
            if (nextPage is { } number && !lastBlock.EndsWith('?'))
            {
                builder.Page(number);
                nextPage = null;
            }
        }

        void EndParagraph()
        {
            if (rows.Count > 0)
            {
                builder.Add(SectionKind.Table, string.Join('\n', rows));
                lastBlock = rows[^1];
                rows.Clear();
            }

            var text = paragraph.ToString();
            paragraph.Clear();
            if (text.Length > 0)
            {
                builder.Add(ListItem().IsMatch(text) ? SectionKind.List : SectionKind.Text, text);
                lastBlock = text;
            }

            SwitchPage();
        }

        foreach (var line in lines)
        {
            if (line.Page != page)
            {
                page = line.Page;
                nextPage = page;
                previous = null;
                if (!ContinuesOnNextPage(paragraph, line))
                {
                    EndParagraph();
                }
            }

            if (IsHeading(line, body))
            {
                EndParagraph();
                lastBlock = string.Empty;
                SwitchPage();
                builder.Heading(headingSizes.IndexOf(Math.Round(line.Size, 1)) + 1, line.Text);
                previous = null;
                continue;
            }

            var gap = previous is null ? 0 : previous.Baseline - line.Baseline;

            // Rows of a table stay rows. A line without columns right under one is a cell that wrapped.
            if (line.Text.Contains(ColumnSeparator, StringComparison.Ordinal) || (rows.Count > 0 && gap <= newParagraphGap))
            {
                if (paragraph.Length > 0)
                {
                    EndParagraph();
                }

                if (line.Text.Contains(ColumnSeparator, StringComparison.Ordinal))
                {
                    rows.Add(line.Text);
                }
                else
                {
                    rows[^1] += " " + line.Text;
                }

                previous = line;
                continue;
            }

            if (previous is not null
                && (gap > newParagraphGap || ListItem().IsMatch(line.Text) || QuestionOrAnswer().IsMatch(line.Text) || previous.Text.EndsWith('?')))
            {
                EndParagraph();
            }

            paragraph.Append(paragraph.Length > 0 ? " " : string.Empty).Append(line.Text);
            previous = line;
        }

        EndParagraph();
        return [.. builder.Sections];
    }

    /// <summary>A sentence cut by the page break goes on: no end mark, and the next line starts in lower case, or it's a Q or A.</summary>
    private static bool ContinuesOnNextPage(StringBuilder paragraph, Line next)
    {
        if (paragraph.Length == 0 || next.Text.Contains(ColumnSeparator, StringComparison.Ordinal) || ".!?:;".Contains(paragraph[^1], StringComparison.Ordinal)
            || ListItem().IsMatch(next.Text) || QuestionOrAnswer().IsMatch(next.Text))
        {
            return false;
        }

        return char.IsLower(next.Text[0]) || QuestionOrAnswer().IsMatch(paragraph.ToString());
    }

    private static bool IsHeading(Line line, double body) =>
        line.Size >= body * HeadingRatio && line.Text.Length <= HeadingMaxLength && !line.Text.EndsWith('.');

    [GeneratedRegex(@"^\s*(?:[-•–·]|\d{1,3}[.)])\s")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s*(?:Q|D|A|R):\s")]
    private static partial Regex QuestionOrAnswer();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}

/// <summary>DOCX (Open XML SDK): heading styles, lists and tables. Only the document's text is read.</summary>
internal sealed partial class DocxParser : IDocumentParser
{
    // A small file can unpack to a huge part (zip bomb): stop reading well before memory is at risk.
    private const long MaxCharactersInPart = 50_000_000;

    public ParsedDocument Parse(byte[] content, string origin)
    {
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(new MemoryStream(content), false, new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or InvalidOperationException)
        {
            throw new SourceReadException(FailureCodes.Unreadable);
        }

        using (document)
        {
            var body = document.MainDocumentPart?.Document?.Body ?? throw new SourceReadException(FailureCodes.Unreadable);
            var builder = new SectionBuilder();
            string? title = null;
            var list = new List<string>();

            void EndList()
            {
                builder.Add(SectionKind.List, string.Join('\n', list));
                list.Clear();
            }

            foreach (var element in body.Elements())
            {
                switch (element)
                {
                    case Paragraph paragraph:
                        var text = paragraph.InnerText.Trim();
                        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                        if (paragraph.ParagraphProperties?.NumberingProperties is not null && text.Length > 0)
                        {
                            list.Add("- " + text);
                            continue;
                        }

                        EndList();
                        if (text.Length == 0)
                        {
                            continue;
                        }

                        if (TitleStyle().IsMatch(style))
                        {
                            title ??= text;
                        }
                        else if (HeadingStyle().Match(style) is { Success: true } heading)
                        {
                            builder.Heading(int.Parse(heading.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), text);
                        }
                        else
                        {
                            builder.Add(SectionKind.Text, text);
                        }

                        break;
                    case Table table:
                        EndList();
                        builder.Add(SectionKind.Table, string.Join('\n', table.Elements<TableRow>()
                            .Select(row => string.Join(" | ", row.Elements<TableCell>().Select(c => c.InnerText.Trim())))));
                        break;
                }
            }

            EndList();
            return new ParsedDocument(title, null, builder.Sections, null, []);
        }
    }

    [GeneratedRegex(@"^(?:Heading|Titolo)\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyle();

    [GeneratedRegex("^(?:Title|Titolo)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleStyle();
}

/// <summary>
/// One web page (SmartReader + AngleSharp): main content only; menus, footers and cookie banners go.
/// Parsing loads nothing further and runs no scripts; the page itself came through SafeHttpClient.
/// </summary>
internal sealed partial class WebPageParser : IDocumentParser
{
    public ParsedDocument Parse(byte[] content, string origin)
    {
        var parser = new HtmlParser();
        // From bytes, so the page's own charset is honoured (many Italian sites aren't UTF-8).
        using var stream = new MemoryStream(content);
        var whole = parser.ParseDocument(stream);
        var article = Reader.ParseArticle(origin, whole.DocumentElement.OuterHtml);
        var builder = new SectionBuilder();

        // The page says where its content is: read that. Otherwise SmartReader finds it, but it also
        // drops short tables, which are often the facts: those are added back. A page too short for
        // SmartReader is read whole, without menus and footers.
        if ((whole.QuerySelector("main") ?? whole.QuerySelector("article")) is { } declared)
        {
            Walk(declared, builder);
        }
        else if (article.IsReadable && !string.IsNullOrWhiteSpace(article.Content))
        {
            var main = parser.ParseDocument(article.Content);
            if (main.Body is not null)
            {
                Walk(main.Body, builder);
            }

            var kept = main.Body?.TextContent ?? string.Empty;
            foreach (var table in whole.QuerySelectorAll("table").Where(t => !InBoilerplate(t) && !kept.Contains(Clean(t.TextContent), StringComparison.Ordinal)))
            {
                AddTable(table, builder);
            }
        }
        else if (whole.Body is not null)
        {
            Walk(whole.Body, builder);
        }

        var title = new[] { article.Title, whole.Title }.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim();
        var language = string.IsNullOrWhiteSpace(article.Language) ? whole.DocumentElement.GetAttribute("lang") : article.Language;
        return new ParsedDocument(title, language, builder.Sections, null, []);
    }

    private static bool IsCookieNotice(IElement element) =>
        CookieNotice().IsMatch($"{element.Id} {element.ClassName}");

    private static bool InBoilerplate(IElement element)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (current.LocalName is "nav" or "footer" or "header" or "aside" or "form" || IsCookieNotice(current))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTable(IElement table, SectionBuilder builder) =>
        builder.Add(SectionKind.Table, string.Join('\n', table.QuerySelectorAll("tr")
            .Select(row => string.Join(" | ", row.Children.Select(c => Clean(c.TextContent))))));

    private static void Walk(IElement parent, SectionBuilder builder)
    {
        foreach (var element in parent.Children)
        {
            if (IsCookieNotice(element))
            {
                continue;
            }

            switch (element.LocalName)
            {
                case "script" or "style" or "noscript" or "nav" or "footer" or "header" or "aside" or "form" or "template":
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    var heading = Clean(element.TextContent);
                    if (heading.Length > 0)
                    {
                        builder.Heading(element.LocalName[1] - '0', heading);
                    }

                    break;
                case "p" or "pre" or "blockquote":
                    builder.Add(SectionKind.Text, Clean(element.TextContent));
                    break;
                case "ul" or "ol":
                    builder.Add(SectionKind.List, string.Join('\n', element.Children
                        .Where(c => c.LocalName == "li")
                        .Select(c => "- " + Clean(c.TextContent))));
                    break;
                case "table":
                    AddTable(element, builder);
                    break;
                default:
                    Walk(element, builder);
                    break;
            }
        }
    }

    private static string Clean(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("cookie|consent|gdpr", RegexOptions.IgnoreCase)]
    private static partial Regex CookieNotice();
}

/// <summary>Plain text: paragraphs between blank lines. A question line with its answer right under it is a FAQ pair.</summary>
internal sealed class TextParser : IDocumentParser
{
    public ParsedDocument Parse(byte[] content, string origin)
    {
        var builder = new SectionBuilder();
        foreach (var paragraph in Encoding.UTF8.GetString(content).ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = paragraph.Trim();
            var lineEnd = clean.IndexOf('\n', StringComparison.Ordinal);
            builder.Add(lineEnd > 0 && clean[lineEnd - 1] == '?' ? SectionKind.Faq : SectionKind.Text, clean);
        }

        return new ParsedDocument(null, null, builder.Sections, null, []);
    }
}
