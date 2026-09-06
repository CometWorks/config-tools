#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CometWorks.ConfigTools;

/// <summary>Runtime diagnostics are advisory: installing files does not require running the game.</summary>
internal static class Prerequisites
{
    internal static bool HasNet10(string runtimes) =>
        Regex.IsMatch(runtimes, @"(?m)^Microsoft\.NETCore\.App 10\.\d+\.\d+(?:\s|$)");

    private static string Runtime()
    {
        var candidates = new[]
        {
            "dotnet",
            OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet",
                    "dotnet.exe"
                )
                : "/usr/share/dotnet/dotnet",
            "/usr/lib/dotnet/dotnet",
            "/usr/local/share/dotnet/dotnet",
        };
        foreach (string command in candidates.Distinct())
        {
            try
            {
                using var process = Process.Start(
                    new ProcessStartInfo(command, "--list-runtimes")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                );
                if (process == null)
                    continue;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    process.Kill();
                    continue;
                }
                if (process.ExitCode == 0 && HasNet10(output.GetAwaiter().GetResult()))
                    return "OK: .NET 10 runtime detected. The tool's bundled runtime is separate.";
            }
            catch (Exception e)
                when (e
                        is System.ComponentModel.Win32Exception
                            or IOException
                            or InvalidOperationException
                ) { }
        }
        return "MISSING: x64 .NET 10 runtime (Microsoft.NETCore.App). Install it to run the launcher; the tool's bundled runtime does not install it system-wide.";
    }

    private static bool Library(string name)
    {
        if (!NativeLibrary.TryLoad(name, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    internal static bool DisplayAvailable(string? driver, bool x11, bool wayland) =>
        string.IsNullOrWhiteSpace(driver)
            ? x11 || wayland
            : driver
                .Split(',')
                .Any(d =>
                    d.Trim() switch
                    {
                        "x11" => x11,
                        "wayland" => wayland,
                        "offscreen" or "dummy" => true,
                        _ => false,
                    }
                );

    public static string Pulsar(string game = "se1")
    {
        var lines = new List<string>
        {
            "Pulsar launch prerequisites (advisory; Steam's runtime may supply missing host libraries):",
            Runtime(),
        };
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
        };
        var libraries = new HashSet<string>(roots.Where(Directory.Exists));
        foreach (string root in roots)
        {
            string vdf = Path.Combine(root, "steamapps/libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                    foreach (
                        Match match in Regex.Matches(
                            File.ReadAllText(vdf),
                            "\"path\"\\s*\"([^\"]+)\""
                        )
                    )
                        libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        lines.Add(
            libraries.Count > 0
                ? "OK: Steam installation detected."
                : "CHECK: Install Steam and sign in as this user (including Flatpak Steam)."
        );
        string app = game == "se2" ? "1133870" : "244850";
        lines.Add(
            libraries.Any(p => File.Exists(Path.Combine(p, "steamapps", $"appmanifest_{app}.acf")))
                ? $"OK: Steam app {app} manifest detected; verify game files in Steam if launch fails."
                : $"CHECK: Install Steam app {app} and select Proton to download its Windows game files."
        );
        if (OperatingSystem.IsLinux())
        {
            foreach (
                var (name, hint) in new[]
                {
                    ("libvulkan.so.1", "Vulkan loader and a working GPU driver"),
                    ("libopus.so.0", "Opus (libopus0 / opus)"),
                }
            )
                lines.Add($"{(Library(name) ? "OK" : "MISSING ON HOST")}: {hint} ({name}).");
            bool x11 = Library("libX11.so.6") && Library("libXext.so.6");
            bool wayland = new[]
            {
                "libwayland-client.so.0",
                "libwayland-cursor.so.0",
                "libwayland-egl.so.1",
                "libxkbcommon.so.0",
            }.All(Library);
            lines.Add(
                $"{(DisplayAvailable(Environment.GetEnvironmentVariable("SDL_VIDEODRIVER"), x11, wayland) ? "OK" : "CHECK")}: display libraries: X11 {(x11 ? "available" : "missing")}, Wayland {(wayland ? "available" : "missing")}. Neither backend is forced; respect SDL_VIDEODRIVER if set."
            );
            lines.Add(
                new[] { "libpipewire-0.3.so.0", "libpulse.so.0", "libasound.so.2" }.Any(Library)
                    ? "OK: audio backend library detected."
                    : "OPTIONAL: PipeWire, PulseAudio or ALSA libraries for sound."
            );
        }
        lines.Add(
            "Bundled SDL3/DXVK/FFmpeg/OpenAL do not need separate installation. GPU access and the active Steam container must be checked at launch."
        );
        return string.Join(Environment.NewLine, lines);
    }

    public static string Magnetar(string? ds64)
    {
        var lines = new List<string> { "Magnetar launch prerequisites (advisory):", Runtime() };
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
            );
            bool framework = key?.GetValue("Release") is int release && release >= 528040;
            lines.Add(
                framework
                    ? "OK: .NET Framework 4.8 or newer detected."
                    : "MISSING: .NET Framework 4.8, needed by the plugin compiler and Legacy launcher on Windows."
            );
        }
        string[] required = OperatingSystem.IsWindows()
            ? new[]
            {
                "SpaceEngineersDedicated.exe",
                "Sandbox.Game.dll",
                "VRage.dll",
                "Steamworks.NET.dll",
                "steam_api64.dll",
            }
            : new[] { "SpaceEngineersDedicated.exe", "Sandbox.Game.dll", "VRage.dll" };
        lines.Add(
            !string.IsNullOrWhiteSpace(ds64)
            && required.All(f => File.Exists(Path.Combine(ds64, f)))
                ? "OK: DedicatedServer64 game files detected."
                : "CHECK: Supply the game's DedicatedServer64 directory (-ds64); it must contain the dedicated server and game assemblies. On Windows it must also contain Steamworks.NET.dll and steam_api64.dll."
        );
        lines.Add(
            "No Steam client, Vulkan, display server, SDK, SteamCMD or external archive utility is needed to install Magnetar. Server game files are installed separately."
        );
        return string.Join(Environment.NewLine, lines);
    }
}
