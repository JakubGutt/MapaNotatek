using Avalonia.Input;

namespace MapaNotatek.Models;

public sealed class ShortcutInfo
{
    public string Keys { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
}

public sealed class AppCommand
{
    public string Name { get; init; } = string.Empty;
    public string Shortcut { get; init; } = string.Empty;
    public Action Run { get; init; } = () => { };
    public Func<KeyEventArgs, bool>? MatchesShortcut { get; init; }
    public bool AllowWhenTyping { get; init; } = true;
}
