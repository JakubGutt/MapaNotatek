using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Platform;
using MapaNotatek.Models;
using SkiaSharp;

namespace MapaNotatek.Services;

/// <summary>
/// Self-contained, offline exporters. No exporter resolves remote URLs.
/// </summary>
public static class NoteExportService
{
    private static readonly Regex ImageLine = new(
        @"^\s*!\[([^\]]*)\]\(([^)]+)\)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(
        @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
        RegexOptions.Compiled);

    public static string BuildMarkdown(Note note)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# " + DisplayTitle(note));
        builder.AppendLine();
        if (note.Tags.Count > 0)
        {
            builder.AppendLine("Tagi: " + string.Join(", ", note.Tags));
            builder.AppendLine();
        }

        builder.Append(CleanBodyForExport(note.Body));
        if (note.Checklist.Count > 0 && !ContainsChecklist(note.Body))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("## Checklista");
            foreach (var item in note.Checklist)
            {
                builder.AppendLine($"- [{(item.IsDone ? "x" : " ")}] {item.Text}");
            }
        }

        AppendExternalLinksMarkdown(builder, note);

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Produces a self-contained fragment suitable for the rich-text clipboard. It never
    /// references remote resources; local images are embedded as data URLs.
    /// </summary>
    public static string BuildConfluenceClipboardHtml(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var title = WebUtility.HtmlEncode(DisplayTitle(note));
        var tags = note.Tags.Count == 0
            ? string.Empty
            : $"<p><em>Tagi: {WebUtility.HtmlEncode(string.Join(", ", note.Tags))}</em></p>";
        return $"<article><h1>{title}</h1>{tags}{BuildHtmlBody(note)}</article>";
    }

    public static string BuildClipboardPlainText(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var builder = new StringBuilder();
        builder.AppendLine(DisplayTitle(note));
        builder.AppendLine();
        if (note.Tags.Count > 0)
        {
            builder.AppendLine("Tagi: " + string.Join(", ", note.Tags));
            builder.AppendLine();
        }

        builder.Append(StripInlineMarkdown(CleanBodyForExport(note.Body)));
        AppendExternalLinksPlainText(builder, note);
        return builder.ToString().TrimEnd();
    }

    public static string BuildHtmlDocument(Note note)
    {
        var title = WebUtility.HtmlEncode(DisplayTitle(note));
        var tags = note.Tags.Count == 0
            ? string.Empty
            : $"<p class=\"tags\">Tagi: {WebUtility.HtmlEncode(string.Join(", ", note.Tags))}</p>";
        var body = BuildHtmlBody(note);

        return $$"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'">
  <title>{{title}}</title>
  <style>
    :root { color-scheme: light dark; }
    body { font-family: Inter, system-ui, sans-serif; max-width: 820px; margin: 48px auto; padding: 0 32px; line-height: 1.62; }
    h1 { font-size: 2.15rem; line-height: 1.18; margin: 0 0 12px; }
    h2 { margin-top: 2rem; } h3 { margin-top: 1.5rem; }
    .tags { opacity: .66; font-size: .85rem; margin-bottom: 2rem; }
    code { background: color-mix(in srgb, currentColor 9%, transparent); padding: .12em .35em; border-radius: 4px; }
    pre { overflow-x: auto; background: color-mix(in srgb, currentColor 7%, transparent); padding: .9rem 1rem; border-radius: 7px; }
    pre code { background: transparent; padding: 0; }
    blockquote { margin-left: 0; border-left: 3px solid #6e88b8; padding-left: 1rem; opacity: .84; }
    table { width: 100%; border-collapse: collapse; margin: 1rem 0; }
    th, td { border: 1px solid #9ba3af; padding: .55rem .7rem; text-align: left; vertical-align: top; }
    img { max-width: 100%; height: auto; display: block; margin: 1.25rem auto; }
    .missing { border: 1px dashed #a36a6a; padding: .75rem; opacity: .75; }
    .task { list-style: none; margin-left: -1.25rem; }
    @media print { body { margin: 0 auto; } }
  </style>
</head>
<body>
  <h1>{{title}}</h1>
  {{tags}}
  {{body}}
</body>
</html>
""";
    }

    public static void WriteDocx(Note note, string path)
    {
        WriteAtomically(path, temporaryPath => WriteDocxCore(note, temporaryPath));
    }

    public static void WritePdf(Note note, string path)
    {
        WriteAtomically(path, temporaryPath => WritePdfCore(note, temporaryPath));
    }

    private static void WriteDocxCore(Note note, string path)
    {
        var images = CollectImages(note);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(archive, "[Content_Types].xml", BuildContentTypes());
        WriteEntry(archive, "_rels/.rels", BuildPackageRelationships());
        WriteEntry(archive, "word/styles.xml", BuildDocxStyles());
        WriteEntry(archive, "word/document.xml", BuildDocxDocument(note, images));
        WriteEntry(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships(images));

        foreach (var image in images)
        {
            var entry = archive.CreateEntry($"word/media/image{image.Index}.png", CompressionLevel.Optimal);
            using var target = entry.Open();
            target.Write(image.PngBytes);
        }
    }

    private static string BuildContentTypes() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
        </Types>
        """;

    private static string BuildPackageRelationships() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private static string BuildDocxStyles() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="38"/><w:szCs w:val="38"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="27"/><w:szCs w:val="27"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:rPr><w:b/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:style>
        </w:styles>
        """;

    private static string BuildDocumentRelationships(IReadOnlyList<ExportImage> images)
    {
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(rel + "Relationships",
            new XElement(rel + "Relationship",
                new XAttribute("Id", "rIdStyles"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml")));
        foreach (var image in images)
        {
            root.Add(new XElement(rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", $"media/image{image.Index}.png")));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildDocxDocument(Note note, IReadOnlyList<ExportImage> images)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

        var body = new XElement(w + "body");
        body.Add(DocxParagraph(DisplayTitle(note), "Title", w));
        if (note.Tags.Count > 0)
        {
            body.Add(DocxParagraph("Tagi: " + string.Join(", ", note.Tags), null, w, color: "667085", italic: true));
        }

        var lines = BuildExportBody(note).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCodeBlock = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                body.Add(DocxParagraph(line, null, w, code: true));
                continue;
            }

            if (index + 1 < lines.Length && LooksLikeTableRow(line) && TableSeparator.IsMatch(lines[index + 1]))
            {
                var rows = new List<string[]> { SplitTableRow(line) };
                index += 2;
                while (index < lines.Length && LooksLikeTableRow(lines[index]))
                {
                    rows.Add(SplitTableRow(lines[index]));
                    index++;
                }

                index--;
                body.Add(DocxTable(rows, w));
                continue;
            }

            var imageMatch = ImageLine.Match(line);
            if (imageMatch.Success)
            {
                var target = imageMatch.Groups[2].Value.Trim();
                var image = images.FirstOrDefault(candidate => candidate.MarkdownTarget == target);
                body.Add(image is null
                    ? DocxParagraph($"[Brak lokalnego obrazu: {imageMatch.Groups[1].Value}]", null, w, color: "9A3412", italic: true)
                    : DocxImageParagraph(image, w, r, wp, a, pic));
                continue;
            }

            var (text, style, prefix, italic, code) = ClassifyMarkdownLine(line);
            body.Add(DocxParagraph(prefix + text, style, w, italic: italic, code: code));
        }

        if (note.Checklist.Count > 0 && !ContainsChecklist(note.Body))
        {
            body.Add(DocxParagraph("Checklista", "Heading2", w));
            foreach (var item in note.Checklist)
            {
                body.Add(DocxParagraph($"{(item.IsDone ? "☑" : "☐")} {item.Text}", null, w));
            }
        }

        body.Add(new XElement(w + "sectPr",
            new XElement(w + "pgSz", new XAttribute(w + "w", 11906), new XAttribute(w + "h", 16838)),
            new XElement(w + "pgMar",
                new XAttribute(w + "top", 1134), new XAttribute(w + "right", 1134),
                new XAttribute(w + "bottom", 1134), new XAttribute(w + "left", 1134))));

        var document = new XElement(w + "document",
            new XAttribute(XNamespace.Xmlns + "w", w),
            new XAttribute(XNamespace.Xmlns + "r", r),
            new XAttribute(XNamespace.Xmlns + "wp", wp),
            new XAttribute(XNamespace.Xmlns + "a", a),
            new XAttribute(XNamespace.Xmlns + "pic", pic),
            body);
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), document)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static XElement DocxParagraph(
        string text,
        string? style,
        XNamespace w,
        string? color = null,
        bool italic = false,
        bool code = false)
    {
        var properties = new XElement(w + "pPr",
            style is null ? null : new XElement(w + "pStyle", new XAttribute(w + "val", style)),
            new XElement(w + "spacing", new XAttribute(w + "after", 120), new XAttribute(w + "line", 300), new XAttribute(w + "lineRule", "auto")));
        var paragraph = new XElement(w + "p", properties);
        if (string.IsNullOrEmpty(text))
        {
            paragraph.Add(new XElement(w + "r", new XElement(w + "t", string.Empty)));
            return paragraph;
        }

        foreach (var span in WikiLinkService.ParseInlineSpans(text))
        {
            var runProperties = new XElement(w + "rPr",
                span.Bold ? new XElement(w + "b") : null,
                span.Italic || italic ? new XElement(w + "i") : null,
                span.Strike ? new XElement(w + "strike") : null,
                span.Code || code ? new XElement(w + "rFonts", new XAttribute(w + "ascii", "Consolas"), new XAttribute(w + "hAnsi", "Consolas")) : null,
                span.Wiki ? new XElement(w + "color", new XAttribute(w + "val", "2459A9")) : null,
                color is null ? null : new XElement(w + "color", new XAttribute(w + "val", color)));
            paragraph.Add(new XElement(w + "r", runProperties,
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), span.Text)));
        }

        return paragraph;
    }

    private static XElement DocxTable(IReadOnlyList<string[]> rows, XNamespace w)
    {
        var table = new XElement(w + "tbl",
            new XElement(w + "tblPr",
                new XElement(w + "tblW", new XAttribute(w + "w", 0), new XAttribute(w + "type", "auto")),
                new XElement(w + "tblBorders",
                    Border("top"), Border("left"), Border("bottom"), Border("right"), Border("insideH"), Border("insideV"))));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new XElement(w + "tr");
            foreach (var cell in rows[rowIndex])
            {
                row.Add(new XElement(w + "tc",
                    new XElement(w + "tcPr", new XElement(w + "tcW", new XAttribute(w + "w", 0), new XAttribute(w + "type", "auto"))),
                    DocxParagraph(cell, null, w, italic: false)));
            }

            table.Add(row);
        }

        return table;

        XElement Border(string name) => new(w + name,
            new XAttribute(w + "val", "single"), new XAttribute(w + "sz", 4), new XAttribute(w + "color", "B8C1CD"));
    }

    private static XElement DocxImageParagraph(
        ExportImage image,
        XNamespace w,
        XNamespace r,
        XNamespace wp,
        XNamespace a,
        XNamespace pic)
    {
        const long maxWidth = 5_800_000;
        var width = Math.Min(maxWidth, Math.Max(914_400, image.Width * 9_525L));
        var height = Math.Max(1, (long)(width * (image.Height / (double)Math.Max(1, image.Width))));
        return new XElement(w + "p",
            new XElement(w + "r",
                new XElement(w + "drawing",
                    new XElement(wp + "inline",
                        new XElement(wp + "extent", new XAttribute("cx", width), new XAttribute("cy", height)),
                        new XElement(wp + "docPr", new XAttribute("id", image.Index), new XAttribute("name", $"Obraz {image.Index}"), new XAttribute("descr", image.AltText)),
                        new XElement(a + "graphic",
                            new XElement(a + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                new XElement(pic + "pic",
                                    new XElement(pic + "nvPicPr",
                                        new XElement(pic + "cNvPr", new XAttribute("id", 0), new XAttribute("name", $"image{image.Index}.png")),
                                        new XElement(pic + "cNvPicPr")),
                                    new XElement(pic + "blipFill",
                                        new XElement(a + "blip", new XAttribute(r + "embed", image.RelationshipId)),
                                        new XElement(a + "stretch", new XElement(a + "fillRect"))),
                                    new XElement(pic + "spPr",
                                        new XElement(a + "xfrm",
                                            new XElement(a + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                            new XElement(a + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                                        new XElement(a + "prstGeom", new XAttribute("prst", "rect"), new XElement(a + "avLst"))))))))));
    }

    private static void WritePdfCore(Note note, string path)
    {
        const float pageWidth = 595.28f;
        const float pageHeight = 841.89f;
        const float margin = 48;
        const float contentTop = 56;
        const float contentBottom = pageHeight - 52;
        const float contentWidth = pageWidth - (margin * 2);
        var textColor = new SKColor(31, 41, 55);
        var mutedColor = new SKColor(100, 116, 139);
        var accentColor = new SKColor(37, 99, 235);
        var subtleBorder = new SKColor(203, 213, 225);
        var surfaceColor = new SKColor(248, 250, 252);

        using var document = SKDocument.CreatePdf(path)
            ?? throw new IOException("Nie można utworzyć dokumentu PDF.");
        using var regular = LoadTypeface("Inter-Regular.ttf");
        using var semibold = LoadTypeface("Inter-SemiBold.ttf");
        using var bold = LoadTypeface("Inter-Bold.ttf");
        using var mono = SKTypeface.FromFamilyName("Menlo") ??
                         SKTypeface.FromFamilyName("Consolas") ??
                         LoadTypeface("Inter-Regular.ttf");

        SKCanvas? canvas = null;
        var pageNumber = 0;
        var y = contentTop;

        SKFont CreateFont(PdfRunStyle style, float size)
        {
            var typeface = style.Code ? mono : style.Bold ? semibold : regular;
            var font = new SKFont(typeface, size);
            if (style.Italic)
            {
                font.SkewX = -0.18f;
            }

            return font;
        }

        string FitText(string text, SKFont font, SKPaint paint, float maxWidth)
        {
            if (font.MeasureText(text, paint) <= maxWidth)
            {
                return text;
            }

            const string suffix = "...";
            var length = text.Length;
            while (length > 0 && font.MeasureText(text[..length] + suffix, paint) > maxWidth)
            {
                length--;
            }

            return text[..length] + suffix;
        }

        void BeginPage()
        {
            canvas = document.BeginPage(pageWidth, pageHeight);
            canvas.Clear(SKColors.White);
            pageNumber++;
            using var headerFont = new SKFont(semibold, 8.2f);
            using var headerPaint = CreatePaint(mutedColor);
            var header = pageNumber == 1 ? "MAPANOTATEK - EKSPORT PDF" : DisplayTitle(note);
            canvas.DrawText(
                FitText(header, headerFont, headerPaint, contentWidth),
                margin,
                28,
                SKTextAlign.Left,
                headerFont,
                headerPaint);
            using var accentPaint = CreatePaint(accentColor);
            canvas.DrawRect(margin, 38, 54, 2.5f, accentPaint);
            using var linePaint = CreatePaint(new SKColor(226, 232, 240));
            canvas.DrawRect(margin + 60, 39, contentWidth - 60, 0.8f, linePaint);
            y = contentTop;
        }

        void EndPage()
        {
            if (canvas is null)
            {
                return;
            }

            using var footerFont = new SKFont(regular, 8.2f);
            using var footerPaint = CreatePaint(new SKColor(130, 138, 150));
            using var footerLine = CreatePaint(new SKColor(226, 232, 240));
            canvas.DrawRect(margin, pageHeight - 39, contentWidth, 0.8f, footerLine);
            canvas.DrawText("Offline - dane lokalne", margin, pageHeight - 22, SKTextAlign.Left, footerFont, footerPaint);
            canvas.DrawText(pageNumber.ToString(), pageWidth - margin, pageHeight - 22, SKTextAlign.Right, footerFont, footerPaint);
            document.EndPage();
            canvas = null;
        }

        void EnsureSpace(float required)
        {
            if (canvas is null)
            {
                BeginPage();
            }
            else if (y + required > contentBottom)
            {
                EndPage();
                BeginPage();
            }
        }

        float MeasureRun(string text, PdfRunStyle style, float size)
        {
            using var font = CreateFont(style, size);
            using var paint = CreatePaint(textColor);
            return font.MeasureText(text, paint) + (style.Code ? 4 : 0);
        }

        List<PdfInlineLine> WrapInline(string text, float size, float maxWidth, bool baseBold = false, bool baseItalic = false)
        {
            var lines = new List<PdfInlineLine> { new() };
            var spans = WikiLinkService.ParseInlineSpans(text).ToList();
            if (spans.Count == 0 && text.Length > 0)
            {
                spans.Add(new InlinePreviewSpan(text, false, false, false, false, false));
            }

            void NewLine()
            {
                if (lines[^1].Runs.Count > 0)
                {
                    lines.Add(new PdfInlineLine());
                }
            }

            void AddToken(string token, PdfRunStyle style)
            {
                if (token.Length == 0)
                {
                    return;
                }

                var whitespace = string.IsNullOrWhiteSpace(token);
                if (whitespace)
                {
                    token = " ";
                    if (lines[^1].Runs.Count == 0)
                    {
                        return;
                    }
                }

                var width = MeasureRun(token, style, size);
                if (lines[^1].Width + width <= maxWidth)
                {
                    lines[^1].Runs.Add(new PdfInlineRun(token, style, width));
                    lines[^1].Width += width;
                    return;
                }

                if (whitespace)
                {
                    NewLine();
                    return;
                }

                if (lines[^1].Runs.Count > 0)
                {
                    NewLine();
                }

                if (width <= maxWidth)
                {
                    lines[^1].Runs.Add(new PdfInlineRun(token, style, width));
                    lines[^1].Width += width;
                    return;
                }

                var fragment = new StringBuilder();
                foreach (var character in token)
                {
                    var candidate = fragment.ToString() + character;
                    if (fragment.Length > 0 && MeasureRun(candidate, style, size) > maxWidth)
                    {
                        var part = fragment.ToString();
                        var partWidth = MeasureRun(part, style, size);
                        lines[^1].Runs.Add(new PdfInlineRun(part, style, partWidth));
                        lines[^1].Width += partWidth;
                        NewLine();
                        fragment.Clear();
                    }

                    fragment.Append(character);
                }

                if (fragment.Length > 0)
                {
                    var part = fragment.ToString();
                    var partWidth = MeasureRun(part, style, size);
                    lines[^1].Runs.Add(new PdfInlineRun(part, style, partWidth));
                    lines[^1].Width += partWidth;
                }
            }

            foreach (var span in spans)
            {
                var style = new PdfRunStyle(
                    baseBold || span.Bold,
                    baseItalic || span.Italic,
                    span.Code,
                    span.Strike,
                    span.Wiki);
                var spanText = span.Wiki && span.Text.StartsWith("[[", StringComparison.Ordinal) && span.Text.EndsWith("]]", StringComparison.Ordinal)
                    ? span.Text[2..^2]
                    : span.Text;
                foreach (Match token in Regex.Matches(spanText, @"\s+|\S+"))
                {
                    AddToken(token.Value, style);
                }
            }

            if (lines.Count > 1 && lines[^1].Runs.Count == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines;
        }

        void DrawInlineLine(PdfInlineLine line, float x, float baseline, float size, SKColor color)
        {
            foreach (var run in line.Runs)
            {
                using var font = CreateFont(run.Style, size);
                var runColor = run.Style.Wiki ? accentColor : color;
                using var paint = CreatePaint(runColor);
                if (run.Style.Code)
                {
                    var glyphWidth = font.MeasureText(run.Text, paint);
                    using var codeBackground = CreatePaint(new SKColor(226, 232, 240));
                    canvas!.DrawRoundRect(
                        new SKRect(x - 2, baseline - (size * 0.9f), x + glyphWidth + 2, baseline + (size * 0.28f)),
                        2.5f,
                        2.5f,
                        codeBackground);
                }

                canvas!.DrawText(run.Text, x, baseline, SKTextAlign.Left, font, paint);
                if (run.Style.Strike)
                {
                    using var strike = CreatePaint(runColor);
                    strike.StrokeWidth = 0.8f;
                    canvas.DrawLine(x, baseline - (size * 0.32f), x + run.Width, baseline - (size * 0.32f), strike);
                }

                x += run.Width;
            }
        }

        void DrawRichWrapped(
            string text,
            float size,
            bool boldText = false,
            bool italicText = false,
            bool muted = false,
            float indent = 0,
            string marker = "",
            float after = 7,
            float before = 0)
        {
            if (before > 0 && y > contentTop + 1)
            {
                y += before;
            }

            using var markerFont = new SKFont(regular, size);
            using var markerPaint = CreatePaint(textColor);
            var markerWidth = marker.Length == 0 ? 0 : Math.Max(18, markerFont.MeasureText(marker, markerPaint) + 7);
            var lineHeight = size * 1.48f;
            var lines = WrapInline(text, size, Math.Max(24, contentWidth - indent - markerWidth), boldText, italicText);
            EnsureSpace((lineHeight * Math.Min(2, Math.Max(1, lines.Count))) + after);
            for (var index = 0; index < lines.Count; index++)
            {
                EnsureSpace(lineHeight);
                var baseline = y + size;
                if (index == 0 && marker.Length > 0)
                {
                    canvas!.DrawText(marker, margin + indent, baseline, SKTextAlign.Left, markerFont, markerPaint);
                }

                DrawInlineLine(
                    lines[index],
                    margin + indent + markerWidth,
                    baseline,
                    size,
                    muted ? mutedColor : textColor);
                y += lineHeight;
            }

            y += after;
        }

        void DrawTags()
        {
            if (note.Tags.Count == 0)
            {
                return;
            }

            using var font = new SKFont(semibold, 8.3f);
            using var textPaint = CreatePaint(new SKColor(30, 64, 175));
            using var fill = CreatePaint(new SKColor(239, 246, 255));
            using var border = CreatePaint(new SKColor(191, 219, 254));
            border.Style = SKPaintStyle.Stroke;
            border.StrokeWidth = 0.8f;
            var x = margin;
            const float height = 20;
            EnsureSpace(height + 12);
            foreach (var tag in note.Tags)
            {
                var label = tag.Trim();
                if (label.Length == 0)
                {
                    continue;
                }

                var width = Math.Min(contentWidth, font.MeasureText(label, textPaint) + 16);
                if (x > margin && x + width > pageWidth - margin)
                {
                    y += height + 5;
                    EnsureSpace(height + 8);
                    x = margin;
                }

                var rect = new SKRect(x, y, x + width, y + height);
                canvas!.DrawRoundRect(rect, 10, 10, fill);
                canvas.DrawRoundRect(rect, 10, 10, border);
                canvas.DrawText(FitText(label, font, textPaint, width - 16), x + 8, y + 13.3f, SKTextAlign.Left, font, textPaint);
                x += width + 6;
            }

            y += height + 13;
        }

        void DrawQuote(string text)
        {
            const float size = 10.5f;
            var lineHeight = size * 1.5f;
            var lines = WrapInline(text, size, contentWidth - 32, baseItalic: true);
            var offset = 0;
            while (offset < Math.Max(1, lines.Count))
            {
                EnsureSpace(lineHeight + 18);
                var fit = Math.Max(1, (int)Math.Floor((contentBottom - y - 16) / lineHeight));
                var count = Math.Min(fit, Math.Max(1, lines.Count - offset));
                var height = (count * lineHeight) + 16;
                using var fill = CreatePaint(new SKColor(248, 250, 252));
                using var accent = CreatePaint(new SKColor(96, 165, 250));
                canvas!.DrawRoundRect(new SKRect(margin, y, pageWidth - margin, y + height), 6, 6, fill);
                canvas.DrawRoundRect(new SKRect(margin, y, margin + 3, y + height), 1.5f, 1.5f, accent);
                for (var lineIndex = 0; lineIndex < count && offset + lineIndex < lines.Count; lineIndex++)
                {
                    DrawInlineLine(lines[offset + lineIndex], margin + 18, y + 8 + size + (lineIndex * lineHeight), size, new SKColor(71, 85, 105));
                }

                y += height + 9;
                offset += count;
                if (offset < lines.Count)
                {
                    EndPage();
                    BeginPage();
                }
            }
        }

        void DrawCodeBlock(string language, IReadOnlyList<string> sourceLines)
        {
            const float size = 8.8f;
            const float lineHeight = 13.2f;
            const float padding = 12;
            using var font = new SKFont(mono, size);
            using var paint = CreatePaint(new SKColor(226, 232, 240));
            var wrapped = new List<string>();
            foreach (var sourceLine in sourceLines.Count == 0 ? [string.Empty] : sourceLines)
            {
                wrapped.AddRange(WrapText(sourceLine.Replace("\t", "    ", StringComparison.Ordinal), font, paint, contentWidth - (padding * 2)));
            }

            var offset = 0;
            var firstChunk = true;
            while (offset < wrapped.Count)
            {
                var labelHeight = firstChunk && !string.IsNullOrWhiteSpace(language) ? 18 : 0;
                EnsureSpace(lineHeight + (padding * 2) + labelHeight);
                var maxLines = Math.Max(1, (int)Math.Floor((contentBottom - y - (padding * 2) - labelHeight) / lineHeight));
                var count = Math.Min(maxLines, wrapped.Count - offset);
                var height = (count * lineHeight) + (padding * 2) + labelHeight;
                using var background = CreatePaint(new SKColor(15, 23, 42));
                canvas!.DrawRoundRect(new SKRect(margin, y, pageWidth - margin, y + height), 7, 7, background);
                if (labelHeight > 0)
                {
                    using var labelFont = new SKFont(semibold, 7.8f);
                    using var labelPaint = CreatePaint(new SKColor(148, 163, 184));
                    canvas.DrawText(language.ToUpperInvariant(), margin + padding, y + 13, SKTextAlign.Left, labelFont, labelPaint);
                }

                var baseline = y + padding + labelHeight + size;
                for (var lineIndex = 0; lineIndex < count; lineIndex++)
                {
                    canvas.DrawText(wrapped[offset + lineIndex], margin + padding, baseline + (lineIndex * lineHeight), SKTextAlign.Left, font, paint);
                }

                y += height + 10;
                offset += count;
                firstChunk = false;
                if (offset < wrapped.Count)
                {
                    EndPage();
                    BeginPage();
                }
            }
        }

        void DrawTable(IReadOnlyList<string[]> sourceRows)
        {
            if (sourceRows.Count == 0)
            {
                return;
            }

            var columnCount = Math.Max(1, sourceRows.Max(row => row.Length));
            var fontSize = columnCount switch
            {
                <= 3 => 8.9f,
                <= 5 => 8.1f,
                <= 7 => 7.3f,
                _ => 6.5f
            };
            var lineHeight = fontSize * 1.42f;
            const float cellPadding = 7;
            using var bodyFont = new SKFont(regular, fontSize);
            using var bodyPaint = CreatePaint(textColor);
            var naturalWidths = new float[columnCount];
            for (var column = 0; column < columnCount; column++)
            {
                naturalWidths[column] = 54;
                foreach (var row in sourceRows.Take(40))
                {
                    var value = column < row.Length ? StripInlineMarkdown(row[column]) : string.Empty;
                    naturalWidths[column] = Math.Max(
                        naturalWidths[column],
                        Math.Min(190, bodyFont.MeasureText(value, bodyPaint) + (cellPadding * 2)));
                }
            }

            var naturalTotal = naturalWidths.Sum();
            var widths = naturalWidths.Select(width => contentWidth * width / naturalTotal).ToArray();

            List<List<string>> WrapRow(string[] row)
            {
                var cells = new List<List<string>>(columnCount);
                for (var column = 0; column < columnCount; column++)
                {
                    var value = column < row.Length ? StripInlineMarkdown(row[column]) : string.Empty;
                    cells.Add(WrapText(value, bodyFont, bodyPaint, Math.Max(12, widths[column] - (cellPadding * 2))));
                }

                return cells;
            }

            var header = WrapRow(sourceRows[0]);

            float RowHeight(IReadOnlyList<List<string>> cells, int offset = 0) =>
                Math.Max(1, cells.Max(cell => Math.Max(0, cell.Count - offset))) * lineHeight + (cellPadding * 2);

            void DrawRowChunk(IReadOnlyList<List<string>> cells, int lineOffset, int lineCount, bool isHeader, bool alternate)
            {
                var height = Math.Max(1, lineCount) * lineHeight + (cellPadding * 2);
                var x = margin;
                using var fill = CreatePaint(isHeader
                    ? new SKColor(219, 234, 254)
                    : alternate ? new SKColor(248, 250, 252) : SKColors.White);
                using var border = CreatePaint(subtleBorder);
                border.Style = SKPaintStyle.Stroke;
                border.StrokeWidth = 0.8f;
                using var headerFont = new SKFont(semibold, fontSize);
                using var headerPaint = CreatePaint(new SKColor(30, 64, 175));
                for (var column = 0; column < columnCount; column++)
                {
                    var rect = new SKRect(x, y, x + widths[column], y + height);
                    canvas!.DrawRect(rect, fill);
                    canvas.DrawRect(rect, border);
                    var cellLines = cells[column];
                    for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
                    {
                        var sourceIndex = lineOffset + lineIndex;
                        if (sourceIndex >= cellLines.Count)
                        {
                            break;
                        }

                        canvas.DrawText(
                            cellLines[sourceIndex],
                            x + cellPadding,
                            y + cellPadding + fontSize + (lineIndex * lineHeight),
                            SKTextAlign.Left,
                            isHeader ? headerFont : bodyFont,
                            isHeader ? headerPaint : bodyPaint);
                    }

                    x += widths[column];
                }

                y += height;
            }

            void DrawHeader()
            {
                var headerLines = Math.Max(1, header.Max(cell => cell.Count));
                DrawRowChunk(header, 0, headerLines, isHeader: true, alternate: false);
            }

            var headerHeight = RowHeight(header);
            EnsureSpace(headerHeight + lineHeight + (cellPadding * 2));
            DrawHeader();
            for (var rowIndex = 1; rowIndex < sourceRows.Count; rowIndex++)
            {
                var cells = WrapRow(sourceRows[rowIndex]);
                var totalLines = Math.Max(1, cells.Max(cell => cell.Count));
                var fullHeight = totalLines * lineHeight + (cellPadding * 2);
                if (fullHeight <= contentBottom - contentTop - headerHeight && y + fullHeight > contentBottom)
                {
                    EndPage();
                    BeginPage();
                    DrawHeader();
                }

                var offset = 0;
                while (offset < totalLines)
                {
                    var availableLines = (int)Math.Floor((contentBottom - y - (cellPadding * 2)) / lineHeight);
                    if (availableLines < 1)
                    {
                        EndPage();
                        BeginPage();
                        DrawHeader();
                        availableLines = Math.Max(1, (int)Math.Floor((contentBottom - y - (cellPadding * 2)) / lineHeight));
                    }

                    var count = Math.Min(availableLines, totalLines - offset);
                    DrawRowChunk(cells, offset, count, isHeader: false, alternate: rowIndex % 2 == 0);
                    offset += count;
                    if (offset < totalLines)
                    {
                        EndPage();
                        BeginPage();
                        DrawHeader();
                    }
                }
            }

            y += 12;
        }

        void DrawImage(Match imageMatch)
        {
            var alt = imageMatch.Groups[1].Value.Trim();
            if (!TryResolveLocalImage(note, imageMatch.Groups[2].Value, out var imagePath))
            {
                DrawRichWrapped($"Brak lokalnego obrazu: {alt}", 9.5f, italicText: true, muted: true, after: 10);
                return;
            }

            using var bitmap = SKBitmap.Decode(imagePath);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                DrawRichWrapped($"Nie można odczytać obrazu: {alt}", 9.5f, italicText: true, muted: true, after: 10);
                return;
            }

            var width = Math.Min(contentWidth * 0.92f, bitmap.Width);
            var height = width * bitmap.Height / bitmap.Width;
            var maxHeight = contentBottom - contentTop - 36;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * bitmap.Width / bitmap.Height;
            }

            if (y + height + 28 > contentBottom && y > contentTop + 1)
            {
                EndPage();
                BeginPage();
            }

            var x = margin + ((contentWidth - width) / 2);
            using var imageBorder = CreatePaint(subtleBorder);
            imageBorder.Style = SKPaintStyle.Stroke;
            imageBorder.StrokeWidth = 0.8f;
            canvas!.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height));
            canvas.DrawRect(new SKRect(x, y, x + width, y + height), imageBorder);
            y += height + 6;
            if (alt.Length > 0)
            {
                DrawRichWrapped(alt, 8.5f, italicText: true, muted: true, after: 10);
            }
            else
            {
                y += 10;
            }
        }

        BeginPage();
        DrawRichWrapped(DisplayTitle(note), 24, boldText: true, after: 7);
        DrawRichWrapped(
            $"Utworzono {note.Created:yyyy-MM-dd}  |  Zmieniono {note.Modified:yyyy-MM-dd}",
            8.6f,
            muted: true,
            after: note.Tags.Count > 0 ? 9 : 18);
        DrawTags();

        var lines = BuildExportBody(note).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            if (rawLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var language = rawLine.Trim()[3..].Trim();
                var codeLines = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[index]);
                    index++;
                }

                DrawCodeBlock(language, codeLines);
                continue;
            }

            if (index + 1 < lines.Length && LooksLikeTableRow(rawLine) && TableSeparator.IsMatch(lines[index + 1]))
            {
                var rows = new List<string[]> { SplitTableRow(rawLine) };
                index += 2;
                while (index < lines.Length && LooksLikeTableRow(lines[index]) && !TableSeparator.IsMatch(lines[index]))
                {
                    rows.Add(SplitTableRow(lines[index]));
                    index++;
                }

                index--;
                DrawTable(rows);
                continue;
            }

            var imageMatch = ImageLine.Match(rawLine);
            if (imageMatch.Success)
            {
                DrawImage(imageMatch);
                continue;
            }

            if (rawLine.Trim() is "---" or "***" or "___")
            {
                EnsureSpace(18);
                using var rule = CreatePaint(subtleBorder);
                canvas!.DrawRect(margin, y + 5, contentWidth, 1, rule);
                y += 18;
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                y += 4;
                continue;
            }

            if (rawLine.StartsWith("### ", StringComparison.Ordinal))
            {
                EnsureSpace(54);
                DrawRichWrapped(rawLine[4..], 12.5f, boldText: true, after: 5, before: 6);
                continue;
            }

            if (rawLine.StartsWith("## ", StringComparison.Ordinal))
            {
                EnsureSpace(70);
                DrawRichWrapped(rawLine[3..], 15.5f, boldText: true, after: 7, before: 9);
                continue;
            }

            if (rawLine.StartsWith("# ", StringComparison.Ordinal))
            {
                EnsureSpace(78);
                DrawRichWrapped(rawLine[2..], 19, boldText: true, after: 8, before: 11);
                continue;
            }

            if (rawLine.StartsWith("> ", StringComparison.Ordinal) || rawLine.Trim() == ">")
            {
                DrawQuote(rawLine.Length > 1 ? rawLine[1..].TrimStart() : string.Empty);
                continue;
            }

            var task = Regex.Match(rawLine, @"^\s*[-*]\s+\[([ xX])\]\s*(.*)$");
            if (task.Success)
            {
                DrawRichWrapped(
                    PersonTagService.StripTaskMetadata(task.Groups[2].Value),
                    10.3f,
                    muted: !string.Equals(task.Groups[1].Value, " ", StringComparison.Ordinal),
                    indent: 10,
                    marker: task.Groups[1].Value == " " ? "[ ]" : "[x]",
                    after: 3);
                continue;
            }

            var bullet = Regex.Match(rawLine, @"^\s*[-*]\s+(.*)$");
            if (bullet.Success)
            {
                DrawRichWrapped(bullet.Groups[1].Value, 10.5f, indent: 10, marker: "•", after: 3);
                continue;
            }

            var numbered = Regex.Match(rawLine, @"^\s*(\d+)[.)]\s+(.*)$");
            if (numbered.Success)
            {
                DrawRichWrapped(numbered.Groups[2].Value, 10.5f, indent: 10, marker: numbered.Groups[1].Value + ".", after: 3);
                continue;
            }

            DrawRichWrapped(rawLine, 10.5f, after: 6);
        }

        if (note.Checklist.Count > 0 && !ContainsChecklist(note.Body))
        {
            DrawRichWrapped("Checklista", 15.5f, boldText: true, after: 7, before: 10);
            foreach (var item in note.Checklist)
            {
                DrawRichWrapped(
                    item.Text,
                    10.3f,
                    muted: item.IsDone,
                    indent: 10,
                    marker: item.IsDone ? "[x]" : "[ ]",
                    after: 3);
            }
        }

        EndPage();
        document.Close();
    }

    private static SKPaint CreatePaint(SKColor color) => new()
    {
        IsAntialias = true,
        Color = color
    };

    private static SKTypeface LoadTypeface(string fileName)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{fileName}"));
            return SKTypeface.FromStream(stream) ?? SKTypeface.Default;
        }
        catch
        {
            return SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        }
    }

    private static List<string> WrapText(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var output = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            output.Add(string.Empty);
            return output;
        }

        var words = text.Split(' ', StringSplitOptions.None);
        var current = new StringBuilder();
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate, paint) <= maxWidth)
            {
                current.Clear();
                current.Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                output.Add(current.ToString());
                current.Clear();
            }

            if (font.MeasureText(word, paint) <= maxWidth)
            {
                current.Append(word);
                continue;
            }

            var fragment = new StringBuilder();
            foreach (var character in word)
            {
                if (fragment.Length > 0 && font.MeasureText(fragment.ToString() + character, paint) > maxWidth)
                {
                    output.Add(fragment.ToString());
                    fragment.Clear();
                }

                fragment.Append(character);
            }

            current.Append(fragment);
        }

        if (current.Length > 0)
        {
            output.Add(current.ToString());
        }

        return output;
    }

    private static string BuildHtmlBody(Note note)
    {
        var builder = new StringBuilder();
        var lines = BuildExportBody(note).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var listKind = string.Empty;
        var inCodeBlock = false;

        void CloseList()
        {
            if (listKind.Length > 0)
            {
                builder.Append("</").Append(listKind).Append('>');
                listKind = string.Empty;
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                CloseList();
                if (inCodeBlock)
                {
                    builder.Append("</code></pre>");
                }
                else
                {
                    builder.Append("<pre><code>");
                }

                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                builder.Append(WebUtility.HtmlEncode(line)).Append('\n');
                continue;
            }

            if (index + 1 < lines.Length && LooksLikeTableRow(line) && TableSeparator.IsMatch(lines[index + 1]))
            {
                CloseList();
                var headers = SplitTableRow(line);
                builder.Append("<table><thead><tr>");
                foreach (var header in headers)
                {
                    builder.Append("<th>").Append(FormatHtmlInline(header)).Append("</th>");
                }

                builder.Append("</tr></thead><tbody>");
                index += 2;
                while (index < lines.Length && LooksLikeTableRow(lines[index]))
                {
                    builder.Append("<tr>");
                    foreach (var cell in SplitTableRow(lines[index]))
                    {
                        builder.Append("<td>").Append(FormatHtmlInline(cell)).Append("</td>");
                    }

                    builder.Append("</tr>");
                    index++;
                }

                index--;
                builder.Append("</tbody></table>");
                continue;
            }

            var image = ImageLine.Match(line);
            if (image.Success)
            {
                CloseList();
                var alt = WebUtility.HtmlEncode(image.Groups[1].Value);
                if (TryResolveLocalImage(note, image.Groups[2].Value, out var imagePath))
                {
                    var mime = MimeForImage(imagePath);
                    var data = Convert.ToBase64String(File.ReadAllBytes(imagePath));
                    builder.Append("<figure><img alt=\"").Append(alt).Append("\" src=\"data:")
                        .Append(mime).Append(";base64,").Append(data).Append("\"></figure>");
                }
                else
                {
                    builder.Append("<p class=\"missing\">Brak lokalnego obrazu: ").Append(alt).Append("</p>");
                }

                continue;
            }

            if (Regex.Match(line, @"^\s*[-*]\s+\[([ xX])\]\s*(.*)$") is { Success: true } task)
            {
                if (listKind != "ul")
                {
                    CloseList();
                    builder.Append("<ul>");
                    listKind = "ul";
                }

                var mark = task.Groups[1].Value == " " ? "☐" : "☑";
                builder.Append("<li class=\"task\">").Append(mark).Append(' ')
                    .Append(FormatHtmlInline(PersonTagService.StripTaskMetadata(task.Groups[2].Value))).Append("</li>");
                continue;
            }

            if (Regex.Match(line, @"^\s*[-*]\s+(.*)$") is { Success: true } bullet)
            {
                if (listKind != "ul")
                {
                    CloseList();
                    builder.Append("<ul>");
                    listKind = "ul";
                }

                builder.Append("<li>").Append(FormatHtmlInline(bullet.Groups[1].Value)).Append("</li>");
                continue;
            }

            if (Regex.Match(line, @"^\s*\d+\.\s+(.*)$") is { Success: true } numbered)
            {
                if (listKind != "ol")
                {
                    CloseList();
                    builder.Append("<ol>");
                    listKind = "ol";
                }

                builder.Append("<li>").Append(FormatHtmlInline(numbered.Groups[1].Value)).Append("</li>");
                continue;
            }

            CloseList();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                builder.Append("<h3>").Append(FormatHtmlInline(line[4..])).Append("</h3>");
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                builder.Append("<h2>").Append(FormatHtmlInline(line[3..])).Append("</h2>");
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                builder.Append("<h1>").Append(FormatHtmlInline(line[2..])).Append("</h1>");
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                builder.Append("<blockquote>").Append(FormatHtmlInline(line[2..])).Append("</blockquote>");
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                builder.Append("<p></p>");
            }
            else
            {
                builder.Append("<p>").Append(FormatHtmlInline(line)).Append("</p>");
            }
        }

        CloseList();
        if (inCodeBlock)
        {
            builder.Append("</code></pre>");
        }

        if (note.Checklist.Count > 0 && !ContainsChecklist(note.Body))
        {
            builder.Append("<h2>Checklista</h2><ul>");
            foreach (var item in note.Checklist)
            {
                builder.Append("<li class=\"task\">").Append(item.IsDone ? "☑ " : "☐ ")
                    .Append(FormatHtmlInline(item.Text)).Append("</li>");
            }

            builder.Append("</ul>");
        }

        return builder.ToString();
    }

    private static string FormatHtmlInline(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        escaped = Regex.Replace(escaped, @"`([^`]+)`", "<code>$1</code>");
        escaped = Regex.Replace(escaped, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        escaped = Regex.Replace(escaped, @"~~([^~]+)~~", "<del>$1</del>");
        escaped = Regex.Replace(escaped, @"(?<!\*)\*([^*]+)\*(?!\*)", "<em>$1</em>");
        escaped = Regex.Replace(escaped, @"\[\[([^\]]+)\]\]", "<strong>$1</strong>");
        return escaped;
    }

    private static IReadOnlyList<ExportImage> CollectImages(Note note)
    {
        var images = new List<ExportImage>();
        foreach (var line in (note.Body ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var match = ImageLine.Match(line);
            if (!match.Success || !TryResolveLocalImage(note, match.Groups[2].Value, out var imagePath))
            {
                continue;
            }

            try
            {
                using var bitmap = SKBitmap.Decode(imagePath);
                if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
                {
                    continue;
                }

                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 95);
                if (encoded is null)
                {
                    continue;
                }

                var index = images.Count + 1;
                images.Add(new ExportImage(
                    index,
                    "rIdImage" + index,
                    match.Groups[2].Value.Trim(),
                    match.Groups[1].Value.Trim(),
                    bitmap.Width,
                    bitmap.Height,
                    encoded.ToArray()));
            }
            catch
            {
                // A broken attachment is represented by a visible placeholder in the document.
            }
        }

        return images;
    }

    private static bool TryResolveLocalImage(Note note, string markdownTarget, out string path)
    {
        path = string.Empty;
        var target = Uri.UnescapeDataString(markdownTarget.Trim().Trim('<', '>'));
        if (string.IsNullOrWhiteSpace(target) || Path.IsPathRooted(target) ||
            Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return false;
        }

        var noteDirectory = Path.GetDirectoryName(note.FilePath);
        var dataRoot = noteDirectory is null ? null : Directory.GetParent(noteDirectory)?.FullName;
        if (noteDirectory is null || dataRoot is null)
        {
            return false;
        }

        var assetsRoot = Path.GetFullPath(Path.Combine(dataRoot, "Assets"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(noteDirectory, target.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static string MimeForImage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private static (string Text, string? Style, string Prefix, bool Italic, bool Code) ClassifyMarkdownLine(string line)
    {
        if (line.StartsWith("### ", StringComparison.Ordinal)) return (line[4..], "Heading3", string.Empty, false, false);
        if (line.StartsWith("## ", StringComparison.Ordinal)) return (line[3..], "Heading2", string.Empty, false, false);
        if (line.StartsWith("# ", StringComparison.Ordinal)) return (line[2..], "Heading1", string.Empty, false, false);
        if (line.StartsWith("> ", StringComparison.Ordinal)) return (line[2..], null, "„ ", true, false);
        if (line.StartsWith("```", StringComparison.Ordinal)) return (string.Empty, null, string.Empty, false, true);
        var task = Regex.Match(line, @"^\s*[-*]\s+\[([ xX])\]\s*(.*)$");
        if (task.Success) return (PersonTagService.StripTaskMetadata(task.Groups[2].Value), null, task.Groups[1].Value == " " ? "☐ " : "☑ ", false, false);
        var bullet = Regex.Match(line, @"^\s*[-*]\s+(.*)$");
        if (bullet.Success) return (bullet.Groups[1].Value, null, "• ", false, false);
        var numbered = Regex.Match(line, @"^\s*(\d+)\.\s+(.*)$");
        if (numbered.Success) return (numbered.Groups[2].Value, null, numbered.Groups[1].Value + ". ", false, false);
        return (line, null, string.Empty, false, false);
    }

    private static bool LooksLikeTableRow(string line) =>
        line.Count(character => character == '|') >= 2;

    private static string[] SplitTableRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (character == '\\' && index + 1 < trimmed.Length && trimmed[index + 1] == '|')
            {
                cell.Append('|');
                index++;
            }
            else if (character == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(character);
            }
        }

        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }

    private static string StripInlineMarkdown(string text)
    {
        var result = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
        result = Regex.Replace(result, @"~~([^~]+)~~", "$1");
        result = Regex.Replace(result, @"\*([^*]+)\*", "$1");
        result = Regex.Replace(result, @"`([^`]+)`", "$1");
        result = Regex.Replace(result, @"\[\[([^\]]+)\]\]", "$1");
        return result;
    }

    private static string BuildExportBody(Note note)
    {
        var builder = new StringBuilder(CleanBodyForExport(note.Body).TrimEnd());
        AppendExternalLinksMarkdown(builder, note);
        return builder.ToString();
    }

    private static string CleanBodyForExport(string? body)
    {
        var normalized = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return string.Join('\n', normalized.Split('\n').Select(PersonTagService.StripTaskMetadata));
    }

    private static void AppendExternalLinksMarkdown(StringBuilder builder, Note note)
    {
        var entries = EnumerateExternalLinks(note).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("## Linki zewnętrzne");
        foreach (var entry in entries)
        {
            builder.Append("- ").Append(entry.Label).Append(": ").AppendLine(entry.Url);
        }
    }

    private static void AppendExternalLinksPlainText(StringBuilder builder, Note note)
    {
        var entries = EnumerateExternalLinks(note).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Linki zewnętrzne:");
        foreach (var entry in entries)
        {
            builder.Append("- ").Append(entry.Label).Append(": ").AppendLine(entry.Url);
        }
    }

    private static IEnumerable<(string Label, string Url)> EnumerateExternalLinks(Note note)
    {
        foreach (var link in note.ExternalLinks)
        {
            yield return (link.Label, link.Url);
        }

        foreach (var task in note.Checklist)
        {
            foreach (var link in task.ExternalLinks)
            {
                var taskLabel = string.IsNullOrWhiteSpace(task.Text) ? "Zadanie" : task.Text.Trim();
                yield return ($"{taskLabel} — {link.Label}", link.Url);
            }
        }
    }

    private static bool ContainsChecklist(string? body) =>
        !string.IsNullOrWhiteSpace(body) && Regex.IsMatch(body, @"(?m)^\s*[-*]\s+\[[ xX]\]\s+");

    private static string DisplayTitle(Note note) =>
        string.IsNullOrWhiteSpace(note.Title) ? "Bez tytułu" : note.Title.Trim();

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content.TrimStart());
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Nie można ustalić folderu eksportu.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.partial");
        try
        {
            write(temporary);
            File.Move(temporary, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // A partial file stays hidden and is never reported as a completed export.
            }

            throw;
        }
    }

    private readonly record struct PdfRunStyle(
        bool Bold,
        bool Italic,
        bool Code,
        bool Strike,
        bool Wiki);

    private readonly record struct PdfInlineRun(string Text, PdfRunStyle Style, float Width);

    private sealed class PdfInlineLine
    {
        public List<PdfInlineRun> Runs { get; } = [];
        public float Width { get; set; }
    }

    private sealed record ExportImage(
        int Index,
        string RelationshipId,
        string MarkdownTarget,
        string AltText,
        int Width,
        int Height,
        byte[] PngBytes);
}
