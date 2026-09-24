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

        builder.Append(note.Body ?? string.Empty);
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

        builder.Append(StripInlineMarkdown(note.Body ?? string.Empty));
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

        var lines = (note.Body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
        const float pageWidth = 595;
        const float pageHeight = 842;
        const float margin = 52;
        const float contentWidth = pageWidth - margin * 2;
        const float bottom = pageHeight - margin;

        using var document = SKDocument.CreatePdf(path)
            ?? throw new IOException("Nie można utworzyć dokumentu PDF.");
        var regular = LoadTypeface("Inter-Regular.ttf");
        var semibold = LoadTypeface("Inter-SemiBold.ttf");
        var mono = SKTypeface.FromFamilyName("Menlo") ?? SKTypeface.FromFamilyName("Consolas") ?? regular;
        using var textPaint = new SKPaint { IsAntialias = true, Color = new SKColor(31, 38, 50) };
        using var mutedPaint = new SKPaint { IsAntialias = true, Color = new SKColor(102, 112, 128) };

        SKCanvas? canvas = null;
        var pageNumber = 0;
        var y = margin;

        void BeginPage()
        {
            canvas = document.BeginPage(pageWidth, pageHeight);
            canvas.Clear(SKColors.White);
            pageNumber++;
            y = margin;
        }

        void EndPage()
        {
            if (canvas is null)
            {
                return;
            }

            using var footerFont = new SKFont(regular, 8.5f);
            using var footerPaint = CreatePaint(new SKColor(130, 138, 150));
            canvas.DrawText(
                $"{DisplayTitle(note)}  ·  {pageNumber}",
                margin,
                pageHeight - 22,
                SKTextAlign.Left,
                footerFont,
                footerPaint);
            document.EndPage();
            canvas = null;
        }

        void EnsureSpace(float required)
        {
            if (canvas is null)
            {
                BeginPage();
            }
            else if (y + required > bottom)
            {
                EndPage();
                BeginPage();
            }
        }

        void DrawWrapped(string text, float size, bool bold = false, bool isMuted = false, float indent = 0, bool isCode = false, float after = 7)
        {
            var typeface = isCode ? mono : bold ? semibold : regular;
            var color = isMuted ? mutedPaint.Color : textPaint.Color;
            using var font = new SKFont(typeface, size);
            using var paint = CreatePaint(color);
            var lineHeight = size * 1.45f;
            var lines = WrapText(text, font, paint, contentWidth - indent);
            if (lines.Count == 0)
            {
                EnsureSpace(lineHeight);
                y += lineHeight;
                return;
            }

            foreach (var wrapped in lines)
            {
                EnsureSpace(lineHeight);
                canvas!.DrawText(wrapped, margin + indent, y + size, SKTextAlign.Left, font, paint);
                y += lineHeight;
            }

            y += after;
        }

        BeginPage();
        DrawWrapped(DisplayTitle(note), 23, bold: true, after: 12);
        if (note.Tags.Count > 0)
        {
            DrawWrapped("Tagi: " + string.Join(", ", note.Tags), 9.5f, isMuted: true, after: 18);
        }

        var inCodeBlock = false;
        foreach (var rawLine in (note.Body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (rawLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                DrawWrapped(rawLine, 9.5f, isCode: true, after: 2);
                continue;
            }

            var imageMatch = ImageLine.Match(rawLine);
            if (imageMatch.Success)
            {
                if (TryResolveLocalImage(note, imageMatch.Groups[2].Value, out var imagePath))
                {
                    using var bitmap = SKBitmap.Decode(imagePath);
                    if (bitmap is not null && bitmap.Width > 0 && bitmap.Height > 0)
                    {
                        var width = Math.Min(contentWidth, bitmap.Width);
                        var height = width * bitmap.Height / bitmap.Width;
                        var maxHeight = pageHeight - margin * 2 - 24;
                        if (height > maxHeight)
                        {
                            height = maxHeight;
                            width = height * bitmap.Width / bitmap.Height;
                        }

                        EnsureSpace(height + 18);
                        canvas!.DrawBitmap(bitmap, new SKRect(margin, y, margin + width, y + height));
                        y += height + 18;
                        continue;
                    }
                }

                DrawWrapped($"[Brak lokalnego obrazu: {imageMatch.Groups[1].Value}]", 10, isMuted: true);
                continue;
            }

            var (text, style, prefix, italic, code) = ClassifyMarkdownLine(rawLine);
            _ = italic;
            var size = style switch
            {
                "Heading1" => 18,
                "Heading2" => 15,
                "Heading3" => 12.5f,
                _ => code ? 9.5f : 10.5f
            };
            DrawWrapped(prefix + StripInlineMarkdown(text), size,
                bold: style is not null,
                indent: prefix.Length > 0 ? 12 : 0,
                isCode: code,
                after: string.IsNullOrWhiteSpace(rawLine) ? 2 : 6);
        }

        if (note.Checklist.Count > 0 && !ContainsChecklist(note.Body))
        {
            DrawWrapped("Checklista", 15, bold: true, after: 8);
            foreach (var item in note.Checklist)
            {
                DrawWrapped($"{(item.IsDone ? "[x]" : "[ ]")} {item.Text}", 10.5f, indent: 12, after: 3);
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
        var lines = (note.Body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
                    .Append(FormatHtmlInline(task.Groups[2].Value)).Append("</li>");
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
        if (task.Success) return (task.Groups[2].Value, null, task.Groups[1].Value == " " ? "☐ " : "☑ ", false, false);
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

    private sealed record ExportImage(
        int Index,
        string RelationshipId,
        string MarkdownTarget,
        string AltText,
        int Width,
        int Height,
        byte[] PngBytes);
}
