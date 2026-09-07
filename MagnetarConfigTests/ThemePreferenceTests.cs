using System;
using System.IO;
using CometWorks.ConfigTools;
using Xunit;

namespace Magnetar.Config.Tests;

public class ThemePreferenceTests
{
    [Fact]
    public void Preference_survives_reloads_and_does_not_depend_on_tool_defaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "theme_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "settings", "theme");
        try
        {
            Assert.Equal(ThemeKind.Turbo, ThemePreference.Load(path, ThemeKind.Turbo));
            Assert.Equal(ThemeKind.Sandstone, ThemePreference.Load(path, ThemeKind.Sandstone));
            Assert.False(Directory.Exists(root));
            ThemePreference.Save(path, ThemeKind.Sandstone);
            Assert.Equal(ThemeKind.Sandstone, ThemePreference.Load(path, ThemeKind.Turbo));
            ThemePreference.Save(path, ThemeKind.Turbo);
            Assert.Equal(ThemeKind.Turbo, ThemePreference.Load(path, ThemeKind.Sandstone));
            foreach (var theme in Enum.GetValues<ThemeKind>())
            {
                ThemePreference.Save(path, theme);
                Assert.Equal(theme, ThemePreference.Load(path, ThemeKind.Sandstone));
            }
            File.WriteAllText(path, "muted\n");
            Assert.Equal(ThemeKind.Sandstone, ThemePreference.Load(path, ThemeKind.Turbo));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)));
            File.WriteAllText(path, "invalid");
            Assert.Equal(ThemeKind.Sandstone, ThemePreference.Load(path, ThemeKind.Sandstone));
            // An unreadable settings location must not prevent startup.
            Assert.Equal(ThemeKind.Turbo, ThemePreference.Load(root, ThemeKind.Turbo));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Quiet_output_uses_default_background_focus_emphasis_and_separate_shadow()
    {
        const string row = "\x1b[4;2H\x1b[40;97mHome\x1b[40;93mStart\x1b[100;37mshadow\x1b[?25l";
        string quiet = PaletteConsole.Translate(row, ThemeKind.Sandstone);
        Assert.Contains("\x1b[22;24;49;38;2;232;222;207mHome", quiet);
        Assert.Contains("\x1b[1;4;49;38;2;209;183;144mStart", quiet);
        Assert.Contains("\x1b[22;24;48;2;0;0;0;38;2;178;163;143mshadow", quiet);
        Assert.StartsWith("\x1b[4;2H", quiet);
        Assert.EndsWith("\x1b[?25l", quiet);
        Assert.Equal(row, PaletteConsole.Translate(row, ThemeKind.Turbo));
        Assert.NotEqual(quiet, PaletteConsole.Translate(row, ThemeKind.Graphite));
        Assert.NotEqual(quiet, PaletteConsole.Translate(row, ThemeKind.Sage));
        Assert.NotEqual(quiet, PaletteConsole.Translate(row, ThemeKind.Plum));
    }
}
