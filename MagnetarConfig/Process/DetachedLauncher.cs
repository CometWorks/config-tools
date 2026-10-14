#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using SysProcess = System.Diagnostics.Process;

namespace Magnetar.Config.Process;

/// <summary>
/// Starts the server on Windows so that it outlives the terminal the tool runs in.
/// When a session ends, terminal hosts kill the process tree under their shell or
/// close a kill-on-close job object. Magnetar's <c>-daemon</c> only frees its
/// console, which covers neither. So the tool runs a copy of itself with
/// <see cref="HelperArg"/>, and that copy creates the server detached and outside
/// the job (when the job allows breakaway), prints its pid and exits. The server
/// is then left with no living parent in the tree.
/// </summary>
internal static class DetachedLauncher
{
    public const string HelperArg = "--spawn-detached";

    private const uint DetachedProcess = 0x00000008;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const int ErrorAccessDenied = 5;

    /// <summary>Starts <paramref name="exe"/> detached and returns its pid.</summary>
    public static int Start(string exe, IReadOnlyList<string> args, string workingDirectory)
    {
        // Only the tool's own executable knows HelperArg. Under a test host the
        // server is created directly: detached and out of the job, but still a child.
        string? self = Environment.ProcessPath;
        if (Assembly.GetEntryAssembly()?.GetName().Name != "MagnetarConfig" || self == null)
            return Create(exe, args, workingDirectory);

        var psi = new ProcessStartInfo(self)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(HelperArg);
        psi.ArgumentList.Add(workingDirectory);
        psi.ArgumentList.Add(exe);
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using SysProcess helper = SysProcess.Start(psi)!;
        // The server inherits no handles, so both streams end when the helper exits.
        var errorRead = helper.StandardError.ReadToEndAsync();
        string output = helper.StandardOutput.ReadToEnd();
        string error = errorRead.Result;
        helper.WaitForExit();
        if (helper.ExitCode == 0 && int.TryParse(output.Trim(), out int pid))
            return pid;
        throw new InvalidOperationException(
            error.Trim() is { Length: > 0 } message
                ? message
                : $"Launch helper exited with code {helper.ExitCode}."
        );
    }

    /// <summary>The helper: arguments are the working directory, the executable and its arguments.</summary>
    public static int RunHelper(string[] args)
    {
        try
        {
            Console.WriteLine(Create(args[1], args[2..], args[0]));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static int Create(string exe, IReadOnlyList<string> args, string workingDirectory)
    {
        string commandLine = string.Join(" ", args.Prepend(exe).Select(Quote));
        uint flags = DetachedProcess | CreateNewProcessGroup;
        // A job that does not allow breakaway refuses the flag; start inside it then.
        if (
            TryCreate(
                commandLine,
                flags | CreateBreakawayFromJob,
                workingDirectory,
                out int pid,
                out int error
            )
            || error == ErrorAccessDenied
                && TryCreate(commandLine, flags, workingDirectory, out pid, out error)
        )
            return pid;
        throw new InvalidOperationException(
            $"Failed to start {exe}: {new Win32Exception(error).Message}"
        );
    }

    private static bool TryCreate(
        string commandLine,
        uint flags,
        string workingDirectory,
        out int pid,
        out int error
    )
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        bool ok = CreateProcessW(
            null,
            new StringBuilder(commandLine),
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            flags,
            IntPtr.Zero,
            workingDirectory,
            ref startup,
            out ProcessInformation info
        );
        error = ok ? 0 : Marshal.GetLastWin32Error();
        pid = ok ? info.dwProcessId : 0;
        if (ok)
        {
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
        }
        return ok;
    }

    /// <summary>Quotes one argument the way CommandLineToArgvW and the C runtime parse it.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved,
            lpDesktop,
            lpTitle;
        public int dwX,
            dwY,
            dwXSize,
            dwYSize,
            dwXCountChars,
            dwYCountChars,
            dwFillAttribute,
            dwFlags;
        public short wShowWindow,
            cbReserved2;
        public IntPtr lpReserved2,
            hStdInput,
            hStdOutput,
            hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess,
            hThread;
        public int dwProcessId,
            dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
