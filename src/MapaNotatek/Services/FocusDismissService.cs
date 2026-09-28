using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MapaNotatek.Services;

/// <summary>
/// Ends text editing when the user clicks outside an editable text field.
/// Registered as a class handler so it also covers dialogs and controls created
/// dynamically at runtime.
/// </summary>
public static class FocusDismissService
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(
            OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void OnPointerPressed(TopLevel topLevel, PointerPressedEventArgs e)
    {
        var focusManager = topLevel.FocusManager;
        if (focusManager?.GetFocusedElement() is not { } focused ||
            FindTextBox(focused as Visual) is null ||
            FindTextBox(e.Source as Visual) is not null)
        {
            return;
        }

        focusManager.Focus(null, NavigationMethod.Pointer, e.KeyModifiers);
    }

    private static TextBox? FindTextBox(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is TextBox textBox)
            {
                return textBox;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }
}
