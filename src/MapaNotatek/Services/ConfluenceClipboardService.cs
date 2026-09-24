using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

/// <summary>
/// Places both plain text and a sanitized HTML fragment on the system clipboard.
/// The user remains in control of the actual paste/publish operation and no network
/// connection is created by the application.
/// </summary>
public static class ConfluenceClipboardService
{
    public static async Task CopyAsync(TopLevel topLevel, Note note)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(note);

        var clipboard = topLevel.Clipboard;
        if (clipboard is null)
        {
            throw new InvalidOperationException("Schowek systemowy nie jest dostępny.");
        }

        var html = NoteExportService.BuildConfluenceClipboardHtml(note);
        var plain = NoteExportService.BuildClipboardPlainText(note);
        var item = new DataTransferItem();
        item.SetText(plain);

        if (OperatingSystem.IsWindows())
        {
            var format = DataFormat.CreateStringPlatformFormat("HTML Format");
            item.Set(format, BuildWindowsHtmlClipboard(html));
        }
        else if (OperatingSystem.IsMacOS())
        {
            var format = DataFormat.CreateStringPlatformFormat("public.html");
            item.Set(format, html);
        }
        else
        {
            var format = DataFormat.CreateStringPlatformFormat("text/html");
            item.Set(format, html);
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    internal static string BuildWindowsHtmlClipboard(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var html = "<html><body>" + startMarker + fragment + endMarker + "</body></html>";
        const string headerTemplate =
            "Version:1.0\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";
        var placeholder = string.Format(headerTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(placeholder);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount("<html><body>" + startMarker);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = startHtml + Encoding.UTF8.GetByteCount(html);
        return string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment) + html;
    }
}
