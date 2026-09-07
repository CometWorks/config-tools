using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Pulsar.Config;

// Keep Terminal.Gui's menu activation and keyboard navigation; hover only paints.
internal sealed class WorkspaceMenuBar(MenuBarItem[] menus) : MenuBar(menus)
{
    public override void Redraw(Rect bounds)
    {
        base.Redraw(bounds);
        // Open menus already track pointer selection in Terminal.Gui.
        if (IsMenuOpen || !PointerHighlight.IsOver(this))
            return;
        int x = PointerHighlight.Column(this);
        int column = 0;
        foreach (var menu in Menus)
        {
            string text = menu.Help.IsEmpty ? $" {menu.Title} " : $" {menu.Title} ({menu.Help}) ";
            string visible = text.Replace("_", "");
            int width = ((NStack.ustring)visible).ConsoleWidth;
            if (x >= column && x < column + width && menu.IsEnabled())
            {
                Move(column, 0);
                Driver.SetAttribute(
                    TerminalTheme.Current == ThemeKind.Turbo
                        ? ColorScheme.Focus
                        : TerminalTheme.HomeAction.Focus
                );
                Driver.AddStr(visible);
                break;
            }
            column += width;
        }
    }
}

// Mouse feedback only: the bar remains outside Tab/arrow-key navigation.
internal sealed class WorkspaceStatusBar : StatusBar
{
    public WorkspaceStatusBar(StatusItem[] items)
        : base(items) => WantMousePositionReports = true;

    internal StatusItem? ItemAt(int x)
    {
        int column = 1;
        foreach (var item in Items)
        {
            int width = (
                (NStack.ustring)
                    (item.Title.ToString() ?? "").Replace(item.HotTextSpecifier.ToString(), "")
            ).ConsoleWidth;
            if (x >= column && x < column + width)
                return item;
            column += width + 3;
        }
        return null;
    }

    public override void Redraw(Rect bounds)
    {
        base.Redraw(bounds);
        if (!PointerHighlight.IsOver(this))
            return;
        int x = PointerHighlight.Column(this);
        int column = 1;
        foreach (var item in Items)
        {
            string text = (item.Title.ToString() ?? "").Replace(
                item.HotTextSpecifier.ToString(),
                ""
            );
            int width = ((NStack.ustring)text).ConsoleWidth;
            if (x >= column && x < column + width && item.IsEnabled())
            {
                Move(column, 0);
                Driver.SetAttribute(
                    TerminalTheme.Current == ThemeKind.Turbo
                        ? ColorScheme.Focus
                        : TerminalTheme.HomeAction.Focus
                );
                Driver.AddStr(text);
                break;
            }
            column += width + 3;
        }
    }
}
