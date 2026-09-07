using Terminal.Gui;

namespace Pulsar.Config;

// Observe input without changing focus, selection, or event handling.
internal sealed class PointerHighlight : IDisposable
{
    private readonly Func<KeyEvent, bool>? previousKey = Application.RootKeyEvent;
    private readonly Action<MouseEvent>? previousMouse = Application.RootMouseEvent;
    private static View? hovered;
    private static Point? position;
    internal static bool MouseActive { get; private set; }

    public PointerHighlight()
    {
        MouseActive = false;
        hovered = null;
        position = null;
        Application.RootKeyEvent = Key;
        Application.RootMouseEvent = Mouse;
    }

    internal static bool IsOver(View view) => MouseActive && hovered == view && view.Enabled;

    internal static int Row(View view) =>
        position is { } point ? view.ScreenToView(point.X, point.Y).Y : -1;

    private bool Key(KeyEvent key)
    {
        if (MouseActive)
        {
            MouseActive = false;
            hovered = null;
            Application.Current?.SetNeedsDisplay();
        }
        return previousKey?.Invoke(key) ?? false;
    }

    private void Mouse(MouseEvent mouse)
    {
        var next = new Point(mouse.X, mouse.Y);
        // Repeated position reports must not override a subsequent keyboard selection.
        if (position != next || mouse.Flags != MouseFlags.ReportMousePosition)
        {
            bool changedMode = !MouseActive;
            MouseActive = true;
            hovered?.SetNeedsDisplay();
            position = next;
            hovered = mouse.View;
            hovered?.SetNeedsDisplay();
            if (changedMode)
                Application.Current?.SetNeedsDisplay();
        }
        previousMouse?.Invoke(mouse);
    }

    public void Dispose()
    {
        Application.RootKeyEvent = previousKey;
        Application.RootMouseEvent = previousMouse;
        MouseActive = false;
        hovered = null;
        position = null;
    }
}
