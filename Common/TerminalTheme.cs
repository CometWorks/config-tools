using Terminal.Gui;

namespace CometWorks.ConfigTools;

/// <summary>Selectable palettes shared by both tools; schemes retain their identity on changes.</summary>
internal static class TerminalTheme
{
    public static ColorScheme Window { get; private set; } = new();
    public static ColorScheme Menu { get; private set; } = new();
    public static ColorScheme Dialog { get; private set; } = new();
    public static ColorScheme Error { get; private set; } = new();
    public static ColorScheme Desktop { get; private set; } = new();

    private static Terminal.Gui.Attribute A(Color fg, Color bg) =>
        Terminal.Gui.Attribute.Make(fg, bg);

    public static ThemeKind Current { get; private set; }
    public static char DesktopGlyph => Current == ThemeKind.Turbo ? '▒' : ' ';
    public static Terminal.Gui.Attribute ReadyColor =>
        Current == ThemeKind.Turbo
            ? A(Color.Black, Color.Green)
            : A(Color.BrightGreen, Color.DarkGray);
    public static Terminal.Gui.Attribute ExceptionColor =>
        Current == ThemeKind.Turbo
            ? A(Color.BrightYellow, Color.Red)
            : A(Color.BrightRed, Color.DarkGray);

    public static void Apply(ThemeKind theme = ThemeKind.Muted)
    {
        Current = theme;
        if (theme == ThemeKind.Turbo)
            ApplyTurbo();
        else
            ApplyMuted();
        Colors.Base = Window;
        Colors.Menu = Menu;
        Colors.Dialog = Dialog;
        Colors.Error = Error;
        Colors.TopLevel = Desktop;
    }

    public static void Choose()
    {
        int choice = MessageBox.Query(
            "Theme",
            "Choose a theme for both tools on this machine.\nCurrent: " + Current,
            "Cancel",
            "Muted",
            "Turbo C"
        );
        if (choice != 1 && choice != 2)
            return;
        var theme = choice == 1 ? ThemeKind.Muted : ThemeKind.Turbo;
        try
        {
            ThemePreference.Save(ThemePreference.FilePath, theme);
        }
        catch (System.Exception error)
            when (error is System.IO.IOException or System.UnauthorizedAccessException)
        {
            MessageBox.ErrorQuery("Theme settings", "Could not save theme: " + error.Message, "OK");
            return;
        }
        Apply(theme);
        Application.Top.SetNeedsDisplay();
    }

    private static void ApplyMuted()
    {
        Set(
            Window,
            new ColorScheme
            {
                Normal = A(Color.White, Color.DarkGray),
                Focus = A(Color.White, Color.Blue),
                HotNormal = A(Color.BrightCyan, Color.DarkGray),
                HotFocus = A(Color.White, Color.Blue),
                Disabled = A(Color.Gray, Color.DarkGray),
            }
        );

        Set(
            Menu,
            new ColorScheme
            {
                Normal = A(Color.Gray, Color.Black),
                Focus = A(Color.White, Color.Blue),
                HotNormal = A(Color.Cyan, Color.Black),
                HotFocus = A(Color.White, Color.Blue),
                Disabled = A(Color.DarkGray, Color.Black),
            }
        );

        Set(
            Dialog,
            new ColorScheme
            {
                Normal = A(Color.White, Color.DarkGray),
                Focus = A(Color.White, Color.Blue),
                HotNormal = A(Color.BrightCyan, Color.DarkGray),
                HotFocus = A(Color.White, Color.Blue),
                Disabled = A(Color.Gray, Color.DarkGray),
            }
        );

        Set(
            Error,
            new ColorScheme
            {
                Normal = A(Color.BrightRed, Color.DarkGray),
                Focus = A(Color.White, Color.Blue),
                HotNormal = A(Color.White, Color.DarkGray),
                HotFocus = A(Color.White, Color.Blue),
                Disabled = A(Color.Gray, Color.DarkGray),
            }
        );

        Set(
            Desktop,
            new ColorScheme
            {
                Normal = A(Color.Gray, Color.Black),
                Focus = A(Color.Gray, Color.Black),
                HotNormal = A(Color.Gray, Color.Black),
                HotFocus = A(Color.Gray, Color.Black),
                Disabled = A(Color.DarkGray, Color.Black),
            }
        );
    }

    private static void ApplyTurbo()
    {
        Set(
            Window,
            new ColorScheme
            {
                Normal = A(Color.White, Color.Blue),
                Focus = A(Color.Black, Color.Cyan),
                HotNormal = A(Color.BrightYellow, Color.Blue),
                HotFocus = A(Color.BrightYellow, Color.Cyan),
                Disabled = A(Color.Gray, Color.Blue),
            }
        );

        Set(
            Menu,
            new ColorScheme
            {
                Normal = A(Color.Black, Color.Gray),
                Focus = A(Color.White, Color.Green),
                HotNormal = A(Color.Red, Color.Gray),
                HotFocus = A(Color.BrightYellow, Color.Green),
                Disabled = A(Color.DarkGray, Color.Gray),
            }
        );

        Set(
            Dialog,
            new ColorScheme
            {
                Normal = A(Color.Black, Color.Gray),
                Focus = A(Color.Black, Color.Cyan),
                HotNormal = A(Color.Blue, Color.Gray),
                HotFocus = A(Color.Blue, Color.Cyan),
                Disabled = A(Color.DarkGray, Color.Gray),
            }
        );

        Set(
            Error,
            new ColorScheme
            {
                Normal = A(Color.White, Color.Red),
                Focus = A(Color.Black, Color.Gray),
                HotNormal = A(Color.BrightYellow, Color.Red),
                HotFocus = A(Color.BrightYellow, Color.Gray),
                Disabled = A(Color.Gray, Color.Red),
            }
        );

        Set(
            Desktop,
            new ColorScheme
            {
                Normal = A(Color.Gray, Color.Blue),
                Focus = A(Color.Gray, Color.Blue),
                HotNormal = A(Color.Gray, Color.Blue),
                HotFocus = A(Color.Gray, Color.Blue),
                Disabled = A(Color.Gray, Color.Blue),
            }
        );
    }

    private static void Set(ColorScheme target, ColorScheme source)
    {
        target.Normal = source.Normal;
        target.Focus = source.Focus;
        target.HotNormal = source.HotNormal;
        target.HotFocus = source.HotFocus;
        target.Disabled = source.Disabled;
    }
}
