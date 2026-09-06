#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;

namespace Magnetar.Config.Install;

internal static class ServerGuard
{
    internal static void RequireStopped(string target)
    {
        if (!Directory.Exists(target)) return;
        if (OperatingSystem.IsWindows())
        {
            if (InstallFiles.Contains(target, InstallFiles.FullPath(AppContext.BaseDirectory)))
                throw new InstallError("Run MagnetarConfig from outside the installation folder before changing it on Windows.");
            Windows(target);
            return;
        }
        foreach (string proc in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(proc), out int pid) || pid == Environment.ProcessId) continue;
            try
            {
                string? executable = new FileInfo(Path.Combine(proc, "exe")).ResolveLinkTarget(true)?.FullName;
                if (executable is not null && InstallFiles.Contains(target, executable)) Running(pid);
                string[] arguments = File.ReadAllText(Path.Combine(proc, "cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                string? cwd = new DirectoryInfo(Path.Combine(proc, "cwd")).ResolveLinkTarget(true)?.FullName;
                foreach (string argument in arguments)
                {
                    if (argument.StartsWith('/') && InstallFiles.Contains(target, argument)) Running(pid);
                    if (cwd is not null && argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && InstallFiles.Contains(target, Path.GetFullPath(argument, cwd))) Running(pid);
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException)
            {
                // A differently privileged server must not be assumed stopped.
                string name;
                try { name = File.ReadAllText(Path.Combine(proc, "comm")).Trim(); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { throw new InstallError($"Cannot inspect process {pid}; verify server processes are stopped before setup."); }
                if (name.StartsWith("Magnetar", StringComparison.OrdinalIgnoreCase) || name == "dotnet")
                    throw new InstallError($"Cannot verify whether process {pid} ({name}) uses this installation. Stop it before setup.");
            }
            catch (IOException) { } // Process exited while /proc was being read.
        }
    }

    private static void Running(int pid) => throw new InstallError($"Stop all servers and tools using this Magnetar installation before setup (PID {pid}).");

    [SupportedOSPlatform("windows")]
    private static void Windows(string target)
    {
        using var query = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
        try
        {
            using var processes = query.Get();
            foreach (ManagementObject process in processes)
            using (process)
            {
                int pid = Convert.ToInt32(process["ProcessId"]);
                if (pid == Environment.ProcessId) continue;
                string name = (string?)process["Name"] ?? "";
                string? exe = (string?)process["ExecutablePath"];
                if (!string.IsNullOrEmpty(exe) && InstallFiles.Contains(target, exe)) Running(pid);
                if (!name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("Magnetar", StringComparison.OrdinalIgnoreCase)) continue;
                string? command = (string?)process["CommandLine"];
                if (command is not null && command.Replace('/', '\\').Contains(target + Path.DirectorySeparatorChar, InstallFiles.Comparison)) Running(pid);
                if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(command)
                    || (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) && !HasAbsoluteAssembly(command)))
                    throw new InstallError($"Cannot verify whether process {pid} ({name}) uses this installation. Stop it before setup.");
            }
        }
        catch (ManagementException error) { throw new InstallError($"Cannot inspect running servers: {error.Message}"); }
    }

    // Relative paths in a shared dotnet host cannot be resolved from WMI's data.
    private static bool HasAbsoluteAssembly(string command) => System.Text.RegularExpressions.Regex.IsMatch(command,
        @"(?:^|\s)(?:""[A-Za-z]:[\\/][^""]+\.dll""|[A-Za-z]:[\\/][^\s""]+\.dll)(?=\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
