#nullable enable
using System;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

// Keep Button's keyboard, hotkey and mouse behavior; only its decoration changes.
internal sealed class WorkspaceButton : Button
{
    private readonly ColorScheme drawingColors = new();

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
        bool highlight =
            Enabled && (PointerHighlight.MouseActive ? PointerHighlight.IsOver(this) : HasFocus);
        var theme = TerminalTheme.HomeAction;
        drawingColors.Normal = highlight ? theme.Focus : theme.Normal;
        drawingColors.Focus = highlight ? theme.Focus : theme.Normal;
        drawingColors.HotNormal = drawingColors.HotFocus = highlight
            ? theme.HotFocus
            : theme.HotNormal;
        drawingColors.Disabled = theme.Disabled;
        if (ColorScheme != drawingColors)
            ColorScheme = drawingColors;
        base.Redraw(bounds);
        if (highlight && bounds.Height >= 3 && TerminalTheme.Current != ThemeKind.Turbo)
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
    public WorkspaceList()
    {
        ColorScheme = TerminalTheme.HomeAction;
        RowRender += row =>
        {
            if (PointerHighlight.MouseActive)
                row.RowAttribute =
                    Enabled
                    && PointerHighlight.IsOver(this)
                    && row.Row == TopItem + PointerHighlight.Row(this)
                        ? ColorScheme.Focus
                        : GetNormalColor();
        };
    }

    public override void Redraw(Rect bounds)
    {
        base.Redraw(bounds);
        int row = PointerHighlight.MouseActive
            ? (PointerHighlight.IsOver(this) ? PointerHighlight.Row(this) : -1)
            : SelectedItem - TopItem;
        if (
            Enabled
            && Source?.Count > 0
            && row >= 0
            && row < bounds.Height
            && TopItem + row < Source.Count
        )
        {
            Driver.SetAttribute(
                PointerHighlight.MouseActive || HasFocus ? ColorScheme.Focus : ColorScheme.HotNormal
            );
            Move(0, row);
            Driver.AddRune('›');
        }
    }
}
