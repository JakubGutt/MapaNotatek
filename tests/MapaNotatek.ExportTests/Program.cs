using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MapaNotatek.Models;
using MapaNotatek.Services;

var root = Path.Combine(Path.GetTempPath(), $"MapaNotatek-export-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(root, "Notes"));
try
{
    var note = new Note
    {
        Id = "export01",
        Title = "Eksport <bezpieczny>",
        FilePath = Path.Combine(root, "Notes", "eksport.md"),
        Tags = ["test", "offline"],
        Body = """
               ## Podsumowanie

               Tekst **ważny** i <script>alert('x')</script>.

               | Pole | Wartość |
               | --- | --- |
               | A | lewa \| prawa |

               ```csharp
               var result = "<offline>";
               ```

               ![zdalny](https://example.invalid/image.png)
               """
    };

    File.WriteAllText(note.FilePath, note.Body);
    VerifyHtml(note);
    VerifyDocx(note, Path.Combine(root, "notatka.docx"));
    VerifyPdf(note, Path.Combine(root, "notatka.pdf"));
    Console.WriteLine("PASS  HTML jest samodzielny i kod jest bezpiecznie kodowany");
    Console.WriteLine("PASS  DOCX ma poprawną strukturę XML i zachowuje kod");
    Console.WriteLine("PASS  PDF powstaje jako niepusty dokument");
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
    Require(bytes.Length > 500, "PDF jest podejrzanie mały.");
    Require(Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-", "Plik nie ma nagłówka PDF.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
