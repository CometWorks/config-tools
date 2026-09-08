using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Pulsar.Config;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulsarConfigTests;

[SupportedOSPlatform("linux")]
public sealed class InstallerTests : IDisposable
{
    private readonly string home = Path.Combine(
        Path.GetTempPath(),
        "pulsar-dotnet-test-" + Guid.NewGuid().ToString("N")
    );
    private readonly Dictionary<string, string?> oldEnvironment = new();
    private readonly Options options;
    private readonly Installer installer;
    private readonly string archive;
    private readonly List<string> log = [];

    public InstallerTests()
    {
        Directory.CreateDirectory(home);
        foreach (
            var (name, value) in new Dictionary<string, string?>
            {
                ["HOME"] = home,
                ["XDG_DATA_HOME"] = home + "/data",
                ["XDG_CONFIG_HOME"] = home + "/config",
                ["XDG_STATE_HOME"] = home + "/state",
                ["XDG_CACHE_HOME"] = home + "/cache",
                ["DOTNET_CLI_HOME"] = home + "/dotnet",
                ["PULSAR_DIR"] = null,
                ["PULSAR_DATA_DIR"] = null,
            }
        )
        {
            oldEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        archive = Package(Path.Combine(home, "release.tar.gz"));
        options = new Options
        {
            Target = Path.Combine(home, "Games/Pulsar with spaces"),
            Archive = archive,
        };
        installer = new Installer(options, log.Add);
    }

    public void Dispose()
    {
        foreach (var (name, value) in oldEnvironment)
            Environment.SetEnvironmentVariable(name, value);
        Directory.Delete(home, true);
    }

    private static string Package(string path, string revision = "first", bool se2 = true)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        using var tar = new TarWriter(gzip);
        foreach (string name in Installer.Required.Concat(se2 ? Installer.Se2Required : []))
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(revision));
            tar.WriteEntry(
                new PaxTarEntry(TarEntryType.RegularFile, "./" + name)
                {
                    DataStream = content,
                    Mode = (UnixFileMode)420,
                }
            );
        }
        return path;
    }

    private void UserFile(string relative, string content) =>
        Files.Write(Path.Combine(options.Target, relative), Encoding.UTF8.GetBytes(content));

    [LinuxFact]
    public async Task Lifecycle_preserves_user_data_and_backups()
    {
        await installer.Run("install");
        Assert.True(
            (File.GetUnixFileMode(options.Target + "/Interim.bin") & UnixFileMode.UserExecute) != 0
        );
        Assert.Equal($"'{options.Target}/Interim.bin' %command%", installer.LaunchOptions);
        UserFile("Legacy/Profiles/Current.xml", "user profile");
        UserFile("notes.txt", "keep me");
        UserFile("Libraries/Interim/updater-added.dll", "owned");
        options.Archive = Package(home + "/new.tar.gz", "second");
        await installer.Run("update");
        Assert.Equal("second", File.ReadAllText(options.Target + "/Interim.bin"));
        string backup = Assert.Single(
            Directory.GetDirectories(
                Path.GetDirectoryName(options.Target)!,
                ".Pulsar with spaces-backup-*"
            )
        );
        Assert.Equal("first", File.ReadAllText(backup + "/Interim.bin"));
        await installer.Run("uninstall");
        Assert.False(Directory.Exists(options.Target + "/Libraries"));
        Assert.False(File.Exists(installer.Desktop));
        Assert.Equal(
            "user profile",
            File.ReadAllText(options.Target + "/Legacy/Profiles/Current.xml")
        );
        Assert.Equal("keep me", File.ReadAllText(options.Target + "/notes.txt"));
        await installer.Run("install");
        Assert.Equal(
            "user profile",
            File.ReadAllText(options.Target + "/Legacy/Profiles/Current.xml")
        );
    }

    [LinuxFact]
    public async Task Se2_selection_and_legacy_receipts_are_preserved()
    {
        options.Game = "se2";
        var se2 = new Installer(options, log.Add);
        await se2.Run("install");
        Assert.EndsWith("/Modern.bin' %command%", se2.LaunchOptions);
        Assert.Contains("1133870", File.ReadAllText(se2.Desktop));
        options.Game = "auto";
        var next = new Installer(options, log.Add);
        await next.Run("update");
        Assert.Equal("se2", next.Game);
        // Same target-hash receipt convention as the Python installer.
        Assert.EndsWith(Files.HashText(options.Target)[..20] + ".json", next.Receipt);
        File.WriteAllText(next.Receipt, "{\"installed\":true}");
        var oldReceipt = new Installer(options, log.Add);
        await oldReceipt.Run("update");
        Assert.Equal("se1", oldReceipt.Game);
    }

    [LinuxFact]
    public async Task Missing_se2_files_do_not_change_install()
    {
        await installer.Run("install");
        options.Game = "se2";
        options.Archive = Package(home + "/old.tar.gz", se2: false);
        await Assert.ThrowsAsync<SetupError>(() => new Installer(options, log.Add).Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

    [LinuxFact]
    public async Task Invalid_checksum_and_missing_archive_preserve_install()
    {
        await installer.Run("install");
        options.Sha256 = new string('0', 64);
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("update"));
        options.Archive = home + "/absent";
        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

    [LinuxTheory]
    [InlineData("../escaped", false)]
    [InlineData("/absolute", false)]
    [InlineData("Legacy/Profiles/Current.xml", false)]
    [InlineData("Libraries/link", true)]
    public async Task Unsafe_archives_never_replace_existing_files(string name, bool link)
    {
        await installer.Run("install");
        string bad = home + "/bad.tar.gz";
        using (var stream = File.Create(bad))
        using (var gzip = new GZipStream(stream, CompressionMode.Compress))
        using (var tar = new TarWriter(gzip))
        {
            var entry = new PaxTarEntry(
                link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile,
                name
            );
            if (link)
                entry.LinkName = "../../outside";
            tar.WriteEntry(entry);
        }
        options.Archive = bad;
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

    [LinuxFact]
    public async Task Unrelated_directory_and_root_are_rejected()
    {
        Directory.CreateDirectory(options.Target);
        UserFile("important", "preserve");
        foreach (string action in new[] { "install", "update", "uninstall" })
            await Assert.ThrowsAsync<SetupError>(() => installer.Run(action));
        Assert.Equal("preserve", File.ReadAllText(options.Target + "/important"));
        Assert.Throws<SetupError>(() => Files.InstallPath(home));
    }

    [LinuxFact]
    public async Task Desktop_failure_restores_install_receipt_and_shortcut()
    {
        await installer.Run("install");
        string receipt = File.ReadAllText(installer.Receipt),
            desktop = File.ReadAllText(installer.Desktop);
        options.Archive = Package(home + "/new.tar.gz", "second");
        installer.Write = (path, data) =>
        {
            if (path == installer.Desktop)
                throw new IOException("Simulated write failure");
            Files.Write(path, data);
        };
        await Assert.ThrowsAsync<IOException>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
        Assert.Equal(receipt, File.ReadAllText(installer.Receipt));
        Assert.Equal(desktop, File.ReadAllText(installer.Desktop));
    }

    [LinuxFact]
    public async Task Other_installation_shortcut_is_not_removed()
    {
        await installer.Run("install");
        File.WriteAllText(
            installer.Desktop,
            "[Desktop Entry]\nX-Pulsar-Install-Path=/another/Pulsar\n"
        );
        await installer.Run("uninstall");
        Assert.True(File.Exists(installer.Desktop));
    }

    [LinuxFact]
    public async Task Update_preserves_private_directory_and_file_permissions()
    {
        await installer.Run("install");
        UserFile("Legacy/config.xml", "private");
        File.SetUnixFileMode(options.Target, (UnixFileMode)448);
        File.SetUnixFileMode(options.Target + "/Legacy", (UnixFileMode)448);
        File.SetUnixFileMode(options.Target + "/Legacy/config.xml", (UnixFileMode)384);
        await installer.Run("update");
        Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(options.Target));
        Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(options.Target + "/Legacy"));
        Assert.Equal(
            (UnixFileMode)384,
            File.GetUnixFileMode(options.Target + "/Legacy/config.xml")
        );
        Files.Write(options.Target + "/Legacy/config.xml", [1]);
        Assert.Equal(
            (UnixFileMode)384,
            File.GetUnixFileMode(options.Target + "/Legacy/config.xml")
        );
    }

    [LinuxFact]
    public async Task Concurrent_operations_are_rejected()
    {
        using var operationLock = Files.Lock(installer.StateDir);
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("install"));
        Assert.False(Directory.Exists(options.Target));
    }

    [LinuxFact]
    public async Task Running_installation_is_detected_by_executable_path()
    {
        await installer.Run("install");
        File.Copy("/bin/sleep", options.Target + "/Interim.bin", true);
        using var process = Process.Start(options.Target + "/Interim.bin", "30")!;
        try
        {
            await Assert.ThrowsAsync<SetupError>(() => installer.Run("uninstall"));
        }
        finally
        {
            process.Kill();
            await process.WaitForExitAsync();
        }
    }

    [LinuxFact]
    public async Task Older_layout_and_removed_migration_are_rejected_without_changes()
    {
        UserFile("Interim", "old wrapper");
        UserFile("Bin/Interim", "old binary");
        foreach (string action in new[] { "install", "update", "uninstall", "migrate" })
            await Assert.ThrowsAsync<SetupError>(() => installer.Run(action));
        Assert.Equal("old wrapper", File.ReadAllText(options.Target + "/Interim"));
        Assert.Equal("old binary", File.ReadAllText(options.Target + "/Bin/Interim"));
        Assert.Throws<SetupError>(() => Options.Parse(["migrate"]));
        Assert.Throws<SetupError>(() => Options.Parse(["--source", home]));
        Assert.Throws<SetupError>(() => Options.Parse(["--settings", home]));
    }

    [LinuxFact]
    public async Task Cancellation_does_not_switch_installation()
    {
        await installer.Run("install");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            installer.Run("update", cancel.Token)
        );
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

}
