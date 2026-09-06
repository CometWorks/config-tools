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
            Assert.Equal(ThemeKind.Muted, ThemePreference.Load(path, ThemeKind.Muted));
            Assert.False(Directory.Exists(root));
            ThemePreference.Save(path, ThemeKind.Muted);
            Assert.Equal(ThemeKind.Muted, ThemePreference.Load(path, ThemeKind.Turbo));
            ThemePreference.Save(path, ThemeKind.Turbo);
            Assert.Equal(ThemeKind.Turbo, ThemePreference.Load(path, ThemeKind.Muted));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)));
            File.WriteAllText(path, "invalid");
            Assert.Equal(ThemeKind.Muted, ThemePreference.Load(path, ThemeKind.Muted));
            // An unreadable settings location must not prevent startup.
            Assert.Equal(ThemeKind.Turbo, ThemePreference.Load(root, ThemeKind.Turbo));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
