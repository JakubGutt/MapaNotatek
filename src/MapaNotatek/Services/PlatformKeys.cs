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
}
