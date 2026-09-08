#nullable enable
using System;
using System.IO;
using CometWorks.ConfigTools;

namespace Magnetar.Config.Install;

internal static class InstallationDiscovery
{
    internal static string StateDirectory
    {
        get
        {
            string state = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg) state = xdg;
            return Path.Combine(state, "config-tools", "magnetar-installer");
        }
    }

    public static InstallationCatalog Create(string? stateDirectory = null, string? historyPath = null) => new(
        "Magnetar", stateDirectory ?? StateDirectory, Probe,
        new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, InstallOptions.DataTarget() }, historyPath);

    private static Installation Probe(string path)
    {
        if (Installer.IsLegacyLinux(path)) return new(path, InstallationKind.Older);
        if (Installer.IsPortable(path))
            return new(path, InstallationKind.Current, OperatingSystem.IsWindows() && File.Exists(Path.Combine(path, "MagnetarLegacy.exe"))
                ? "Legacy (.NET Framework) / Interim (.NET)" : "Interim (.NET)");
        bool partial = File.Exists(Path.Combine(path, "MagnetarInterim.dll")) || File.Exists(Path.Combine(path, "MagnetarLegacy.exe"));
        return new(path, partial ? InstallationKind.Incomplete : InstallationKind.Unknown);
    }
}
