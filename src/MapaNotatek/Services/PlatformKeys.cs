using System.Runtime.InteropServices;
using Avalonia.Input;

namespace MapaNotatek.Services;

/// <summary>
/// Platform-aware primary modifier: ⌘ on macOS, Ctrl on Windows/Linux.
/// </summary>
public static class PlatformKeys
{
    public static bool IsMac { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string ModLabel { get; } = IsMac ? "⌘" : "Ctrl";
    public static string ModShiftLabel { get; } = IsMac ? "⌘⇧" : "Ctrl+Shift";
    public static string RedoLabel { get; } = IsMac ? "⌘⇧Z" : "Ctrl+Y";
    public static string TrashLabel { get; } = IsMac ? "⌫" : "Delete";

    public static string Chord(string key) => $"{ModLabel}+{key}";
    public static string ChordShift(string key) => $"{ModShiftLabel}+{key}";

    /// <summary>True when the platform command key is held (Cmd on Mac, Ctrl elsewhere).</summary>
    public static bool IsCommand(KeyModifiers modifiers) =>
        IsMac
            ? modifiers.HasFlag(KeyModifiers.Meta)
            : modifiers.HasFlag(KeyModifiers.Control);

    /// <summary>True only for the platform command modifier, optionally with Shift.</summary>
    public static bool IsExactCommand(KeyModifiers modifiers, bool shift)
    {
        var allowed = (IsMac ? KeyModifiers.Meta : KeyModifiers.Control) |
                      (shift ? KeyModifiers.Shift : KeyModifiers.None);
        return modifiers == allowed;
    }

    /// <summary>
    /// AltGr is represented as Ctrl+Alt on Windows. Some input paths briefly report
    /// only Ctrl, so a non-ASCII printable key symbol is also treated as composed text.
    /// </summary>
    public static bool IsTextComposition(KeyEventArgs e, bool rightAltHeld)
    {
        if (!IsWindows)
        {
            return false;
        }

        if (rightAltHeld ||
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            return true;
        }

        return !string.IsNullOrEmpty(e.KeySymbol) &&
               e.KeySymbol.Any(character => !char.IsControl(character) && character > 127);
    }
}
