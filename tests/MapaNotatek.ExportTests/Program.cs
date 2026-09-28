using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MapaNotatek.Models;
using MapaNotatek.Services;
using SkiaSharp;

var root = Path.Combine(Path.GetTempPath(), $"MapaNotatek-export-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(root, "Notes"));
try
{
    var note = new Note
    {
        Id = "export01",
        Title = "Przegląd kwartalny - eksport PDF",
        FilePath = Path.Combine(root, "Notes", "eksport.md"),
        Tags = ["raport", "offline", "zespół-produktowy"],
        Body = BuildPdfFixtureBody()
    };

    CreateFixtureImage(root, note.Id);
    File.WriteAllText(note.FilePath, note.Body);
    VerifyHtml(note);
    VerifyDocx(note, Path.Combine(root, "notatka.docx"));
    var requestedPdfPath = Environment.GetEnvironmentVariable("MAPANOTATEK_PDF_FIXTURE_PATH");
    var pdfPath = string.IsNullOrWhiteSpace(requestedPdfPath)
        ? Path.Combine(root, "notatka.pdf")
        : Path.GetFullPath(requestedPdfPath);
    Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
    VerifyPdf(note, pdfPath);
    Console.WriteLine("PASS  HTML jest samodzielny i kod jest bezpiecznie kodowany");
    Console.WriteLine("PASS  DOCX ma poprawną strukturę XML i zachowuje kod");
    Console.WriteLine("PASS  PDF powstaje jako niepusty dokument");
    if (!string.IsNullOrWhiteSpace(requestedPdfPath))
    {
        Console.WriteLine("PDF QA: " + pdfPath);
    }
    Console.WriteLine("\nWynik: 3/3 testów eksportu zaliczonych.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL  test eksportu");
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyHtml(Note note)
{
    var html = NoteExportService.BuildHtmlDocument(note);
    Require(html.Contains("default-src 'none'", StringComparison.Ordinal), "HTML nie ma polityki offline.");
    Require(html.Contains("<pre><code>", StringComparison.Ordinal), "Blok kodu nie został wyrenderowany.");
    Require(html.Contains("&lt;offline&gt;", StringComparison.Ordinal), "Kod nie został zakodowany jako tekst.");
    Require(!html.Contains("<script>", StringComparison.OrdinalIgnoreCase), "Treść dokumentu wstrzyknęła skrypt.");
    Require(!html.Contains("src=\"https://", StringComparison.OrdinalIgnoreCase), "HTML odwołuje się do zdalnego obrazu.");

    var fragment = NoteExportService.BuildConfluenceClipboardHtml(note);
    Require(!fragment.Contains("<script>", StringComparison.OrdinalIgnoreCase), "Schowek rich text zawiera skrypt.");
    Require(!fragment.Contains("src=\"https://", StringComparison.OrdinalIgnoreCase), "Schowek rich text zawiera zdalny obraz.");
}

static void VerifyDocx(Note note, string path)
{
    NoteExportService.WriteDocx(note, path);
    using var archive = ZipFile.OpenRead(path);
    var required = new[]
    {
        "[Content_Types].xml",
        "_rels/.rels",
        "word/document.xml",
        "word/styles.xml",
        "word/_rels/document.xml.rels"
    };
    foreach (var name in required)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Brak elementu DOCX: " + name);
        using var stream = entry.Open();
        _ = XDocument.Load(stream);
    }

    var documentEntry = archive.GetEntry("word/document.xml")!;
    using var documentStream = documentEntry.Open();
    using var reader = new StreamReader(documentStream, Encoding.UTF8);
    var documentXml = reader.ReadToEnd();
    Require(documentXml.Contains("Consolas", StringComparison.Ordinal), "Blok kodu nie ma stylu monospace w DOCX.");
    Require(!documentXml.Contains("```", StringComparison.Ordinal), "Znaczniki Markdown trafiły do DOCX.");
}

static void VerifyPdf(Note note, string path)
{
    NoteExportService.WritePdf(note, path);
    var bytes = File.ReadAllBytes(path);
    Require(bytes.Length > 10_000, "PDF jest podejrzanie mały.");
    Require(Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-", "Plik nie ma nagłówka PDF.");
    var ascii = Encoding.ASCII.GetString(bytes);
    var pages = System.Text.RegularExpressions.Regex.Matches(ascii, @"/Type\s*/Page(?!s)").Count;
    Require(pages >= 2, "Rozbudowany dokument powinien poprawnie przechodzić na kolejne strony.");
}

static string BuildPdfFixtureBody()
{
    var builder = new StringBuilder();
    builder.AppendLine("# Podsumowanie zarządcze");
    builder.AppendLine();
    builder.AppendLine("To jest **ważny fragment**, *kursywa*, ~~nieaktualna informacja~~, `wartość_inline` oraz [[Powiązana notatka]]. Tekst zawiera polskie znaki: ąęćłńóśźż i sprawdza bezpieczne kodowanie <script>alert('x')</script>.");
    builder.AppendLine();
    builder.AppendLine("> Najważniejszy wniosek powinien być widoczny od razu, ale nie dominować nad właściwą treścią dokumentu.");
    builder.AppendLine();
    builder.AppendLine("## Plan działania");
    builder.AppendLine();
    builder.AppendLine("- Pierwszy punkt z dłuższym opisem, który musi zawinąć się do kolejnego wiersza bez utraty wcięcia i znacznika listy.");
    builder.AppendLine("- Drugi punkt z **pogrubieniem** oraz kodem `status=ready`.");
    builder.AppendLine("1. Etap przygotowania");
    builder.AppendLine("2. Etap wdrożenia");
    builder.AppendLine("- [x] Zamknięte zadanie");
    builder.AppendLine("- [ ] Otwarte zadanie wymagające dalszej pracy");
    builder.AppendLine();
    builder.AppendLine("## Tabela wyników");
    builder.AppendLine();
    builder.AppendLine("| Obszar | Właściciel | Status | Opis i następny krok |");
    builder.AppendLine("| --- | --- | --- | --- |");
    for (var index = 1; index <= 28; index++)
    {
        builder.AppendLine($"| Moduł {index:00} | Zespół {(char)('A' + (index % 5))} | {(index % 3 == 0 ? "Ryzyko" : "W toku")} | Szczegółowy opis pozycji numer {index}, zawierający dłuższą treść, która powinna estetycznie zawinąć się wewnątrz komórki tabeli. |");
    }

    builder.AppendLine();
    builder.AppendLine("## Przykład kodu");
    builder.AppendLine();
    builder.AppendLine("```csharp");
    builder.AppendLine("public static string BuildReport(IEnumerable<Item> items)");
    builder.AppendLine("{");
    builder.AppendLine("    var active = items.Where(item => item.IsActive);");
    builder.AppendLine("    return string.Join(\", \", active.Select(item => item.Name));");
    builder.AppendLine("}");
    builder.AppendLine("var result = \"<offline>\";");
    builder.AppendLine("```");
    builder.AppendLine();
    builder.AppendLine("## Diagram lokalny");
    builder.AppendLine();
    builder.AppendLine("![Przepływ pracy](../Assets/export01/diagram.png)");
    builder.AppendLine();
    builder.AppendLine("![zdalny](https://example.invalid/image.png)");
    builder.AppendLine();
    builder.AppendLine("---");
    builder.AppendLine();
    builder.AppendLine("### Koniec dokumentu");
    builder.AppendLine("Ostatni akapit kontroluje stopkę, odstępy i końcowe łamanie strony.");
    return builder.ToString();
}

static void CreateFixtureImage(string root, string noteId)
{
    var folder = Path.Combine(root, "Assets", noteId);
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "diagram.png");
    using var bitmap = new SKBitmap(900, 420);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(new SKColor(241, 245, 249));
    using var blue = new SKPaint { IsAntialias = true, Color = new SKColor(37, 99, 235) };
    using var teal = new SKPaint { IsAntialias = true, Color = new SKColor(13, 148, 136) };
    using var line = new SKPaint { IsAntialias = true, Color = new SKColor(100, 116, 139), StrokeWidth = 6 };
    canvas.DrawRoundRect(new SKRect(70, 120, 330, 300), 28, 28, blue);
    canvas.DrawLine(330, 210, 570, 210, line);
    canvas.DrawRoundRect(new SKRect(570, 120, 830, 300), 28, 28, teal);
    using var image = SKImage.FromBitmap(bitmap);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 95);
    using var stream = File.Create(path);
    encoded.SaveTo(stream);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
