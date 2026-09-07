using Terminal.Gui;

namespace Pulsar.Config;

// Global navigation unwinds modal screens before running the selected action.
// Closing handlers can defer leaving until an operation has cancelled safely.
internal sealed class GlobalShortcuts : IDisposable
{
    private readonly Toplevel owner;
    private readonly WorkspaceStatusBar bar;
    private readonly Func<KeyEvent, bool>? previousKey = Application.RootKeyEvent;
    private readonly Action<MouseEvent>? previousMouse = Application.RootMouseEvent;
    private Action? pending;

    public GlobalShortcuts(Toplevel owner)
    {
        this.owner = owner;
        bar = (WorkspaceStatusBar)owner.StatusBar;
        Application.RootKeyEvent = Key;
        Application.RootMouseEvent = Mouse;
        Application.Iteration += ContinueNavigation;
    }

    private bool Key(KeyEvent key)
    {
        if (previousKey?.Invoke(key) == true)
            return true;
        bar.SetNeedsDisplay();
        var item = bar.Items.FirstOrDefault(item => item.Shortcut == key.Key);
        if (item is null)
            return false;
        Navigate(item);
        return true;
    }

    private void Mouse(MouseEvent mouse)
    {
        bool onBar = bar.Visible && mouse.Y == Application.Driver.Rows - 1;
        if (onBar)
            mouse.View = bar;
        previousMouse?.Invoke(mouse);
        if (!onBar)
            return;
        mouse.Handled = true;
        if (mouse.Flags == MouseFlags.Button1Clicked && bar.ItemAt(mouse.X) is { } item)
            Navigate(item);
    }

    private void Navigate(StatusItem item)
    {
        if (!item.IsEnabled())
            return;
        // Changing theme keeps the active editor and its unsaved fields open.
        if (item.Shortcut == Terminal.Gui.Key.F2)
        {
            if (owner.MenuBar.IsMenuOpen)
                owner.MenuBar.ProcessKey(new KeyEvent(Terminal.Gui.Key.Esc, new KeyModifiers()));
            Application.MainLoop.Invoke(item.Action);
            return;
        }
        pending = item.Action;
        ContinueNavigation();
    }

    private void ContinueNavigation()
    {
        if (pending is null || Application.Current is null)
            return;
        if (Application.Current != owner)
        {
            Application.RequestStop();
            return;
        }
        if (owner.MenuBar.IsMenuOpen)
            owner.MenuBar.ProcessKey(new KeyEvent(Terminal.Gui.Key.Esc, new KeyModifiers()));
        var action = pending;
        pending = null;
        Application.MainLoop.Invoke(action);
    }

    public void Dispose()
    {
        Application.Iteration -= ContinueNavigation;
        Application.RootKeyEvent = previousKey;
        Application.RootMouseEvent = previousMouse;
        pending = null;
    }
}
