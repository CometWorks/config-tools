using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Pulsar.Config;

internal static class WindowsProcessGuard
{
    [SupportedOSPlatform("windows")]
    internal static void RequireStopped(string[] roots)
    {
        using var query = new ManagementObjectSearcher(
            "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
        try
        {
            using var processes = query.Get();
            foreach (ManagementObject process in processes)
            using (process)
            {
                int pid = Convert.ToInt32(process["ProcessId"]);
                if (pid == Environment.ProcessId)
                    continue;
                string name = (string?)process["Name"] ?? "";
                string? exe = (string?)process["ExecutablePath"];
                string? command = (string?)process["CommandLine"];
                if (exe is not null && roots.Any(root => Files.Contains(root, exe)))
                    throw new SetupError($"Close Pulsar and its game before continuing (PID {pid}).");
                bool hosted = name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
                if (!hosted && !new[] { "Interim.exe", "Modern.exe", "Legacy.exe", "SpaceEngineers.exe", "SpaceEngineers2.exe" }
                    .Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (command is not null && roots.Any(root => command.Replace('/', '\\')
                    .Contains(root + Path.DirectorySeparatorChar, Files.Comparison)))
                    throw new SetupError($"Close Pulsar and its game before continuing (PID {pid}).");
                // WMI cannot resolve the working directory of a relative dotnet assembly.
                string? assembly = command is null ? null : Regex.Matches(command, "\"([^\"]*)\"|(\\S+)")
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .Skip(1).FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(command)
                    || (hosted && assembly is not null && !Path.IsPathFullyQualified(assembly)))
                    throw new SetupError($"Cannot inspect process {pid} ({name}); close it before changing Pulsar.");
            }
        }
        catch (ManagementException error)
        {
            throw new SetupError($"Cannot inspect running processes: {error.Message}");
        }
    }
}
