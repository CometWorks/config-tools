using System;
using System.IO;

namespace CometWorks.ConfigTools;

internal enum ThemeKind
{
    Sandstone,
    Graphite,
    Sage,
    Plum,
    Turbo,
}

/// <summary>User-local appearance preference, independent of installs and server/game profiles.</summary>
internal static class ThemePreference
{
    public static string FilePath
    {
        get
        {
            string root;
            if (OperatingSystem.IsWindows())
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            else
            {
                root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
                if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root))
                    root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config"
                    );
            }
            return Path.Combine(root, "CometWorks", "config-tools", "theme");
        }
    }

    public static ThemeKind Load(string path, ThemeKind fallback)
    {
        try
        {
            return File.ReadAllText(path).Trim() switch
            {
                "muted" or "sandstone" => ThemeKind.Sandstone,
                "graphite" => ThemeKind.Graphite,
                "sage" => ThemeKind.Sage,
                "plum" => ThemeKind.Plum,
                "turbo" => ThemeKind.Turbo,
                _ => fallback,
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public static void Save(string path, ThemeKind theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Path.GetRandomFileName();
        try
        {
            File.WriteAllText(temp, theme.ToString().ToLowerInvariant() + "\n");
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
