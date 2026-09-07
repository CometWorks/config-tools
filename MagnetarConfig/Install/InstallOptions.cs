#nullable enable
using System;
using System.IO;

namespace Magnetar.Config.Install;

internal sealed class InstallOptions
{
    public string Target = DefaultTarget();
    public string Version = "latest";
    public string? Action, Archive, Sha256, Ds64;
    public bool CheckDependencies = true, Yes;

    public static string DefaultTarget()
    {
        string adjacent = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (Installer.IsPortable(adjacent)) return adjacent;
        string data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg)
            data = xdg;
        return Path.Combine(data, "Magnetar");
    }

    public static InstallOptions Parse(string[] args)
    {
        var options = new InstallOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value() => ++i < args.Length ? args[i] : throw new InstallError($"Missing value for {arg}.");
            switch (arg)
            {
                case "install" or "update" or "uninstall" or "check" when options.Action is null: options.Action = arg; break;
                case "--target": options.Target = Value(); break;
                case "--version": options.Version = Value(); break;
                case "--archive": options.Archive = Value(); break;
                case "--sha256": options.Sha256 = Value(); break;
                case "--ds64" or "-ds64": options.Ds64 = Value(); break;
                case "--yes": options.Yes = true; break;
                default: throw new InstallError($"Unknown setup argument: {arg}");
            }
        }
        return options;
    }
}

internal sealed class InstallError(string message) : Exception(message);
