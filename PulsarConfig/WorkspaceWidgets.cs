using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Pulsar.Config;

// Keep Button's keyboard, hotkey and mouse behavior; only its decoration changes.
internal sealed class WorkspaceButton : Button
{
    public WorkspaceButton(string label)
        : base(label)
    {
        AutoSize = false;
        Width = label.Replace("_", "").Length + 6;
        Height = 1;
        TextAlignment = TextAlignment.Left;
        ColorScheme = TerminalTheme.HomeAction;
    }

    protected override void UpdateTextFormatterText()
    {
        if (TerminalTheme.Current == ThemeKind.Turbo)
            base.UpdateTextFormatterText();
        else
            TextFormatter.Text = "  " + Text;
    }

    public override void Redraw(Rect bounds)
    {
        UpdateTextFormatterText();
        base.Redraw(bounds);
        if (HasFocus && bounds.Height >= 3 && TerminalTheme.Current != ThemeKind.Turbo)
        {
            Driver.SetAttribute(TerminalTheme.Window.HotNormal);
            DrawFrame(bounds, 0, false);
        }
    }
}

internal sealed class WorkspaceWindow : Window
{
    public WorkspaceWindow(string title)
        : base(title) { }

    public override void Redraw(Rect bounds)
    {
        Border.BorderBrush = TerminalTheme.Border.Normal.Foreground;
        Border.Background = TerminalTheme.Border.Normal.Background;
        base.Redraw(bounds);
    }
}

internal sealed class WorkspaceDialog : Dialog
{
    public WorkspaceDialog(string title, int width = 0, int height = 0, params Button[] buttons)
        : base(title, width, height, buttons) { }

    public override void Redraw(Rect bounds)
    {
        var colors =
            TerminalTheme.Current == ThemeKind.Turbo ? TerminalTheme.Dialog : TerminalTheme.Border;
        Border.BorderBrush = colors.Normal.Foreground;
        Border.Background = colors.Normal.Background;
        base.Redraw(bounds);
    }
}

// Rows reserve two leading cells so selection remains visible without a solid background.
internal sealed class WorkspaceList : ListView
{
    public WorkspaceList() => ColorScheme = TerminalTheme.HomeAction;

    public override void Redraw(Rect bounds)
    {
        base.Redraw(bounds);
        int row = SelectedItem - TopItem;
        if (Source?.Count > 0 && row >= 0 && row < bounds.Height)
        {
            Driver.SetAttribute(HasFocus ? ColorScheme.Focus : ColorScheme.HotNormal);
            Move(0, row);
            Driver.AddRune('›');
        }
    }
}
