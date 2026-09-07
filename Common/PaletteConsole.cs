#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CometWorks.ConfigTools;

/// <summary>
/// Terminal.Gui 1.19's NetDriver emits complete rows with 16-colour SGR pairs.
/// Translate those pairs at the output boundary; leave input and terminal palettes alone.
/// </summary>
internal sealed class PaletteConsole : TextWriter
{
    private readonly TextWriter output;
    private static readonly Regex Attribute = new(@"\x1b\[(4[0-7]|10[0-7]);(3[0-7]|9[0-7])m");
    private bool attached;
    private ThemeKind? renderedTheme;

    private PaletteConsole(TextWriter output) => this.output = output;

    public override Encoding Encoding => output.Encoding;

    public static PaletteConsole Attach()
    {
        var writer = new PaletteConsole(Console.Out) { attached = true };
        Console.SetOut(writer);
        return writer;
    }

    internal static string Translate(string text, ThemeKind theme)
    {
        if (theme == ThemeKind.Turbo)
            return text;
        var (normal, muted, accent) = theme switch
        {
            ThemeKind.Graphite => ("227;229;227", "147;155;153", "184;195;189"),
            ThemeKind.Sage => ("224;231;223", "160;178;159", "181;200;165"),
            ThemeKind.Plum => ("232;223;233", "179;160;183", "197;171;201"),
            _ => ("232;222;207", "178;163;143", "209;183;144"),
        };
        return Attribute.Replace(
            text,
            match =>
            {
                string foreground = match.Groups[2].Value;
                string rgb = foreground switch
                {
                    "37" or "90" => muted,
                    "33" or "93" => accent,
                    "91" => "224;145;137",
                    "92" => "170;196;150",
                    _ => normal,
                };
                // SGR 49 uses the terminal's configured background (including transparency).
                // Terminal.Gui's default 3D shadow uses bright-black background, kept black.
                string background = match.Groups[1].Value == "100" ? "48;2;0;0;0" : "49";
                string emphasis = foreground == "93" ? "1;4" : "22;24";
                return $"\x1b[{emphasis};{background};38;2;{rgb}m";
            }
        );
    }

    public override void Write(string? value)
    {
        if (value is not null)
        {
            if (renderedTheme != TerminalTheme.Current)
            {
                // Turbo's original SGR pairs don't clear a quiet theme's focus underline.
                output.Write("\x1b[0m");
                renderedTheme = TerminalTheme.Current;
            }
            output.Write(Translate(value, TerminalTheme.Current));
        }
    }

    public override void Write(char value) => output.Write(value);

    public override void Write(char[] buffer, int index, int count) =>
        Write(new string(buffer, index, count));

    public override void Write(ReadOnlySpan<char> buffer) => Write(buffer.ToString());

    public override void Flush() => output.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing && attached)
        {
            attached = false;
            output.Write("\x1b[0m");
            output.Flush();
            Console.SetOut(output);
        }
        base.Dispose(disposing);
    }
}
