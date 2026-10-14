#nullable enable
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Magnetar.Config.Process;
using Xunit;
using SysProcess = System.Diagnostics.Process;

namespace Magnetar.Config.Tests;

public class DetachedLauncherTests
{
    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData(@"C:\Program Files (x86)\Steam\DedicatedServer64")]
    [InlineData(@"C:\trailing\")]
    [InlineData(@"C:\trailing space\")]
    [InlineData("say \"hi\"")]
    [InlineData(@"back\\""slash")]
    [InlineData("tab\there")]
    public void Quote_round_trips_through_the_windows_parser(string arg)
    {
        if (!OperatingSystem.IsWindows())
            return;
        string[] parsed = Parse("exe.exe " + DetachedLauncher.Quote(arg));
        Assert.Equal(new[] { "exe.exe", arg }, parsed);
    }

    [Fact]
    public void Started_process_is_detached_and_reports_its_pid()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // Under the test host the helper is skipped and the process is created directly.
        int pid = DetachedLauncher.Start(
            Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\ping.exe"),
            ["-n", "3", "127.0.0.1"],
            Environment.CurrentDirectory
        );
        using SysProcess ping = SysProcess.GetProcessById(pid);
        Assert.Equal("PING", ping.ProcessName.ToUpperInvariant());
        Assert.True(ping.WaitForExit(10_000));
    }

    private static string[] Parse(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        try
        {
            return Enumerable
                .Range(0, count)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
