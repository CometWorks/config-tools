using Terminal.Gui;

namespace CometWorks.ConfigTools;

/// <summary>Shared neutral terminal palette with blue focus and cyan shortcuts.</summary>
internal static class TerminalTheme
{
    public static ColorScheme Window { get; private set; } = null!;
    public static ColorScheme Menu { get; private set; } = null!;
    public static ColorScheme Dialog { get; private set; } = null!;
    public static ColorScheme Error { get; private set; } = null!;
    public static ColorScheme Desktop { get; private set; } = null!;

    private static Terminal.Gui.Attribute A(Color fg, Color bg) =>
        Terminal.Gui.Attribute.Make(fg, bg);

    public static void Apply()
    {
        Window = new ColorScheme
        {
            Normal = A(Color.White, Color.DarkGray),
            Focus = A(Color.White, Color.Blue),
            HotNormal = A(Color.BrightCyan, Color.DarkGray),
            HotFocus = A(Color.White, Color.Blue),
            Disabled = A(Color.Gray, Color.DarkGray),
        };

        Menu = new ColorScheme
        {
            Normal = A(Color.Gray, Color.Black),
            Focus = A(Color.White, Color.Blue),
            HotNormal = A(Color.Cyan, Color.Black),
            HotFocus = A(Color.White, Color.Blue),
            Disabled = A(Color.DarkGray, Color.Black),
        };

        Dialog = new ColorScheme
        {
            Normal = A(Color.White, Color.DarkGray),
            Focus = A(Color.White, Color.Blue),
            HotNormal = A(Color.BrightCyan, Color.DarkGray),
            HotFocus = A(Color.White, Color.Blue),
            Disabled = A(Color.Gray, Color.DarkGray),
        };

        Error = new ColorScheme
        {
            Normal = A(Color.BrightRed, Color.DarkGray),
            Focus = A(Color.White, Color.Blue),
            HotNormal = A(Color.White, Color.DarkGray),
            HotFocus = A(Color.White, Color.Blue),
            Disabled = A(Color.Gray, Color.DarkGray),
        };

        Desktop = new ColorScheme
        {
            Normal = A(Color.Gray, Color.Black),
            Focus = A(Color.Gray, Color.Black),
            HotNormal = A(Color.Gray, Color.Black),
            HotFocus = A(Color.Gray, Color.Black),
            Disabled = A(Color.DarkGray, Color.Black),
        };

        Colors.Base = Window;
        Colors.Menu = Menu;
        Colors.Dialog = Dialog;
        Colors.Error = Error;
        Colors.TopLevel = Desktop;
    }
}
