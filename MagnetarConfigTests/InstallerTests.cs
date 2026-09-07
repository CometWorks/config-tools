#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Magnetar.Config.Install;
using SharpCompress.Writers.SevenZip;
using Xunit;

namespace Magnetar.Config.Tests;

public sealed class InstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "magnetar-setup-test-" + Guid.NewGuid().ToString("N"));
    private readonly InstallOptions options;
    private readonly Installer installer;
    private readonly List<string> reports = new();

    public InstallerTests()
    {
        Directory.CreateDirectory(root);
        options = new InstallOptions { Target = Path.Combine(root, "Magnetar server"), CheckDependencies = false };
        options.Archive = Package("first.7z", "first");
        installer = new Installer(options, reports.Add, Path.Combine(root, "setup-state"));
    }

    public void Dispose() => Directory.Delete(root, true);

    private string Package(string name, string revision, string? unexpected = null)
    {
        string path = Path.Combine(root, name);
        using var output = File.Create(path);
        using var writer = new SevenZipWriter(output, new SevenZipWriterOptions());
        foreach (string relative in new[] { "LICENSE", "README.md", "MagnetarInterim.dll", "MagnetarInterim.deps.json", "MagnetarInterim.runtimeconfig.json",
                     "MagnetarInterim.bin", "MagnetarInterim.exe", "MagnetarLegacy.exe", "MagnetarLegacy.exe.config",
                     "Libraries/MagnetarInterim/Pulsar.Shared.dll", "Libraries/MagnetarLegacy/Pulsar.Shared.dll",
                     "Libraries/MagnetarInterim/Steamworks.NET.dll", "Libraries/MagnetarInterim/libsteam_api.so",
                     "Libraries/Compiler/Compiler.bin", "Libraries/Compiler/Compiler.exe", "MagnetarConfig.exe", "Libraries/MagnetarConfig/Terminal.Gui.dll" })
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(revision));
            writer.Write("Magnetar/" + relative, content, null);
        }
        if (unexpected is not null)
        {
            using var content = new MemoryStream([1, 2, 3]);
            writer.Write(unexpected, content, null);
        }
        return path;
    }

    private void UserFile(string name, string value)
    {
        string path = Path.Combine(options.Target, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }

    [Fact]
    public async Task Lifecycle_preserves_instances_worlds_tools_and_unrelated_libraries()
    {
        await installer.Run("install");
        Assert.False(File.Exists(Path.Combine(options.Target, "MagnetarConfig.exe")));
        Assert.False(Directory.Exists(Path.Combine(options.Target, "Libraries/MagnetarConfig")));
        string[] userFiles = ["Magnetar/Profiles/Current.xml", "MagnetarLegacy/config.xml", "MagnetarInterim/config.xml",
            "Saves/World/Sandbox.sbc", "SpaceEngineers-Dedicated.cfg", "notes.txt", "Libraries/Custom/plugin.dll",
            "MagnetarConfig.exe", "Libraries/MagnetarConfig/Terminal.Gui.dll"];
        foreach (string file in userFiles) UserFile(file, "private user data");
        UserFile("Libraries/MagnetarInterim/obsolete.dll", "old package");
        options.Archive = Package("second.7z", "second");
        await installer.Run("update");
        Assert.Equal("second", File.ReadAllText(Path.Combine(options.Target, "MagnetarInterim.dll")));
        Assert.False(File.Exists(Path.Combine(options.Target, "Libraries/MagnetarInterim/obsolete.dll")));
        string backup = Assert.Single(Directory.GetDirectories(root, ".Magnetar server-backup-*"));
        Assert.Equal("first", File.ReadAllText(Path.Combine(backup, "MagnetarInterim.dll")));
        await installer.Run("uninstall");
        Assert.False(File.Exists(Path.Combine(options.Target, "MagnetarInterim.dll")));
        Assert.False(Directory.Exists(Path.Combine(options.Target, "Libraries/Compiler")));
        foreach (string file in userFiles) Assert.Equal("private user data", File.ReadAllText(Path.Combine(options.Target, file)));
        await installer.Run("install");
        foreach (string file in userFiles) Assert.Equal("private user data", File.ReadAllText(Path.Combine(options.Target, file)));
    }

    [Fact]
    public async Task Receipt_failure_rolls_back_files_and_previous_receipt()
    {
        await installer.Run("install");
        byte[] receipt = File.ReadAllBytes(installer.Receipt);
        options.Archive = Package("new.7z", "new");
        installer.WriteReceipt = (_, _) => throw new IOException("Simulated state write failure");
        await Assert.ThrowsAsync<IOException>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(Path.Combine(options.Target, "MagnetarInterim.dll")));
        Assert.Equal(receipt, File.ReadAllBytes(installer.Receipt));
        Assert.Empty(Directory.GetDirectories(root, ".Magnetar server-setup-*"));
    }

    [Fact]
    public async Task New_install_failure_leaves_no_target_or_receipt()
    {
        installer.WriteReceipt = (_, _) => throw new IOException("Simulated state write failure");
        await Assert.ThrowsAsync<IOException>(() => installer.Run("install"));
        Assert.False(Directory.Exists(options.Target));
        Assert.False(File.Exists(installer.Receipt));
    }

    [Fact]
    public async Task Invalid_hash_and_corrupt_archive_leave_current_installation_unchanged()
    {
        await installer.Run("install");
        options.Sha256 = new string('0', 64);
        await Assert.ThrowsAsync<InstallError>(() => installer.Run("update"));
        options.Sha256 = null;
        options.Archive = Path.Combine(root, "corrupt.7z");
        File.WriteAllText(options.Archive, "Not a 7z archive");
        await Assert.ThrowsAnyAsync<Exception>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(Path.Combine(options.Target, "MagnetarInterim.dll")));
    }

    [Theory]
    [InlineData("Magnetar/../escaped")]
    [InlineData("Magnetar/Saves/world.sbc")]
    [InlineData("Magnetar/Libraries/MagnetarInterim/../../escaped")]
    public async Task Unsafe_entries_never_change_existing_installation(string entry)
    {
        await installer.Run("install");
        options.Archive = Package("unsafe.7z", "new", entry);
        await Assert.ThrowsAsync<InstallError>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(Path.Combine(options.Target, "MagnetarInterim.dll")));
        Assert.False(File.Exists(Path.Combine(root, "escaped")));
    }

    [Fact]
    public async Task Unknown_and_old_linux_installations_are_not_changed()
    {
        UserFile("important.txt", "keep");
        await Assert.ThrowsAsync<InstallError>(() => installer.Run("install"));
        UserFile("MagnetarInterim", "old wrapper");
        UserFile("Bin/MagnetarInterim", "old binary");
        var error = await Assert.ThrowsAsync<InstallError>(() => installer.Run("update"));
        Assert.Contains("migration", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(options.Target, "important.txt")));
        Assert.Equal("old binary", File.ReadAllText(Path.Combine(options.Target, "Bin/MagnetarInterim")));
    }

    [Fact]
    public async Task Lock_and_cancellation_prevent_switching()
    {
        await installer.Run("install");
        using (var operationLock = new FileStream(Path.Combine(installer.StateDirectory, "setup.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<InstallError>(() => installer.Run("update"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => installer.Run("update", cancel.Token));
        Assert.Equal("first", File.ReadAllText(Path.Combine(options.Target, "MagnetarInterim.dll")));
    }

    [Theory]
    [InlineData("dotnet test Tests/Tests.csproj -c Release", false)]
    [InlineData("dotnet exec C:\\server\\MagnetarInterim.dll", false)]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\server folder\\MagnetarInterim.dll\"", false)]
    [InlineData("dotnet exec .\\MagnetarInterim.dll", true)]
    [InlineData("dotnet MagnetarInterim.dll --argument C:\\other\\dependency.dll", true)]
    public void Windows_process_check_distinguishes_sdk_commands_and_hosted_assemblies(string command, bool expected)
        => Assert.Equal(expected, ServerGuard.HasRelativeAssembly(command));

    [Fact]
    public async Task Running_native_server_blocks_update()
    {
        await installer.Run("install");
        string source = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/sleep";
        string executable = Path.Combine(options.Target, OperatingSystem.IsWindows() ? "running-test.exe" : "MagnetarInterim.bin");
        File.Copy(source, executable, true);
        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo(executable, OperatingSystem.IsWindows() ? "/c ping -n 30 127.0.0.1 > nul" : "30") { UseShellExecute = false })!;
        try { await Assert.ThrowsAsync<InstallError>(() => installer.Run("update")); }
        finally { process.Kill(true); await process.WaitForExitAsync(); }
    }

    [Fact]
    public async Task Shared_dotnet_host_with_relative_assembly_blocks_update()
    {
        await installer.Run("install");
        // Build a tiny sleeping server with the SDK already running these tests.
        // Keep its source outside the install; invoke its managed DLL relatively.
        string source = Path.Combine(root, "probe-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><UseAppHost>false</UseAppHost></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(source, "Program.cs"), "class Program { static void Main() => System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); }");
        File.WriteAllText(Path.Combine(source, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var build = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "build", Path.Combine(source, "Probe.csproj"), "--output", options.Target, "--nologo" }) build.ArgumentList.Add(argument);
        using (var compiler = System.Diagnostics.Process.Start(build)!)
        {
            Task<string> output = compiler.StandardOutput.ReadToEndAsync(), errors = compiler.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await compiler.WaitForExitAsync(timeout.Token); }
            finally { if (!compiler.HasExited) compiler.Kill(true); }
            Assert.True(compiler.ExitCode == 0, await output + await errors);
        }
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = options.Target, UseShellExecute = false };
        start.ArgumentList.Add("Probe.dll");
        using var process = System.Diagnostics.Process.Start(start)!;
        try { await Assert.ThrowsAsync<InstallError>(() => installer.Run("update")); }
        finally { process.Kill(true); await process.WaitForExitAsync(); }
    }

    [Fact]
    public async Task Dependency_check_does_not_create_state_or_installation()
    {
        await installer.Run("check");
        Assert.False(Directory.Exists(installer.StateDirectory));
        Assert.False(Directory.Exists(options.Target));
        Assert.Contains(reports, report => report.Contains("prerequisites", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Linux_private_modes_and_state_symlinks_survive_update()
    {
        if (!OperatingSystem.IsLinux()) return;
        await installer.Run("install");
        UserFile("Magnetar/config.xml", "secret");
        File.SetUnixFileMode(options.Target, (UnixFileMode)448);
        File.SetUnixFileMode(Path.Combine(options.Target, "Magnetar"), (UnixFileMode)448);
        File.SetUnixFileMode(Path.Combine(options.Target, "Magnetar/config.xml"), (UnixFileMode)384);
        string external = Path.Combine(root, "external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "plugin.dll"), "external");
        Directory.CreateSymbolicLink(Path.Combine(options.Target, "Local"), external);
        await installer.Run("update");
        Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(options.Target));
        Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(Path.Combine(options.Target, "Magnetar")));
        Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(Path.Combine(options.Target, "Magnetar/config.xml")));
        Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(installer.Receipt));
        Assert.Equal(external, new DirectoryInfo(Path.Combine(options.Target, "Local")).LinkTarget);
        Assert.Equal("external", File.ReadAllText(Path.Combine(external, "plugin.dll")));
    }

    [Fact]
    public async Task Symlinked_library_root_cannot_redirect_deletion()
    {
        if (!OperatingSystem.IsLinux()) return;
        await installer.Run("install");
        string external = Path.Combine(root, "external-libraries");
        Directory.Move(Path.Combine(options.Target, "Libraries"), external);
        Directory.CreateSymbolicLink(Path.Combine(options.Target, "Libraries"), external);
        await Assert.ThrowsAsync<InstallError>(() => installer.Run("uninstall"));
        Assert.True(File.Exists(Path.Combine(external, "MagnetarInterim/Pulsar.Shared.dll")));
    }
}
