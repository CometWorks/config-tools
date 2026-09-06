using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Missing_se2_files_do_not_change_install()
    {
        await installer.Run("install");
        options.Game = "se2";
        options.Archive = Package(home + "/old.tar.gz", se2: false);
        await Assert.ThrowsAsync<SetupError>(() => new Installer(options, log.Add).Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

    [Fact]
    public async Task Invalid_checksum_and_missing_archive_preserve_install()
    {
        await installer.Run("install");
        options.Sha256 = new string('0', 64);
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("update"));
        options.Archive = home + "/absent";
        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(options.Target + "/Interim.bin"));
    }

    [Theory]
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

    [Fact]
    public async Task Unrelated_directory_and_root_are_rejected()
    {
        Directory.CreateDirectory(options.Target);
        UserFile("important", "preserve");
        foreach (string action in new[] { "install", "update", "uninstall" })
            await Assert.ThrowsAsync<SetupError>(() => installer.Run(action));
        Assert.Equal("preserve", File.ReadAllText(options.Target + "/important"));
        Assert.Throws<SetupError>(() => Files.InstallPath(home));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Concurrent_operations_are_rejected()
    {
        using var operationLock = Files.Lock(installer.StateDir);
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("install"));
        Assert.False(Directory.Exists(options.Target));
    }

    [Fact]
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

    private string LegacySettings()
    {
        string state = Files.OldConfig,
            dev = home + "/dev/se-remote";
        Files.Write(
            dev + "/Remote.xml",
            Encoding.UTF8.GetBytes("<PluginData><Id>remote</Id></PluginData>")
        );
        Files.Write(state + "/config.xml", Encoding.UTF8.GetBytes("<CoreConfig/>"));
        Files.Write(state + "/Local/Custom.dll", [1, 2, 3]);
        Files.Write(state + "/Sources/Hubs/obsolete.bin", [4, 5, 6]);
        var sources = new XElement(
            "SourcesConfig",
            new XElement(
                "RemoteHubSources",
                new XElement("RemoteHub", new XElement("Hash", "stale"))
            ),
            new XElement(
                "LocalPluginSources",
                new XElement(
                    "LocalPlugin",
                    new XElement("Name", "se-remote"),
                    new XElement("Folder", dev),
                    new XElement("File", ""),
                    new XElement("Enabled", true)
                ),
                new XElement(
                    "LocalPlugin",
                    new XElement("Name", "se-linux-compat"),
                    new XElement("Folder", home + "/missing/se-linux-compat"),
                    new XElement("Enabled", true)
                )
            )
        );
        Files.Write(state + "/Sources/sources.xml", Encoding.UTF8.GetBytes(sources.ToString()));
        Files.Write(
            state + "/Profiles/Current.xml",
            Encoding.UTF8.GetBytes(
                """
                <Profile><Name>Current</Name><GitHub><GitHubPluginConfig><Id>se-dotnet-compat</Id></GitHubPluginConfig></GitHub>
                <DevFolder><LocalFolderConfig><Id>se-linux-compat</Id><DataFile>LinuxCompatClient.xml</DataFile></LocalFolderConfig>
                <LocalFolderConfig><Id>se-remote</Id><DataFile>Remote.xml</DataFile><DebugBuild>true</DebugBuild></LocalFolderConfig></DevFolder>
                <Mods><unsignedLong>123456</unsignedLong></Mods></Profile>
                """
            )
        );
        return state;
    }

    private void AssertMigrated(string original)
    {
        string root = options.Target + "/Legacy";
        Assert.Equal(
            File.ReadAllBytes(original + "/Local/Custom.dll"),
            File.ReadAllBytes(root + "/Local/Custom.dll")
        );
        Assert.False(Directory.Exists(root + "/Sources/Hubs"));
        Assert.True(File.Exists(original + "/Sources/Hubs/obsolete.bin"));
        var source = XDocument.Load(root + "/Sources/sources.xml");
        Assert.Empty(source.Descendants("Hash"));
        Assert.Equal(
            "Remote.xml",
            source.Descendants("LocalPlugin").First().Element("File")!.Value
        );
        Assert.Equal("false", source.Descendants("LocalPlugin").Last().Element("Enabled")!.Value);
        var profile = XDocument.Load(root + "/Profiles/Current.xml");
        Assert.Equal(
            "remote",
            Assert.Single(profile.Descendants("LocalFolderConfig")).Element("Id")!.Value
        );
        Assert.Empty(profile.Descendants("DataFile"));
        Assert.Equal(
            "dotnet-compat",
            profile.Descendants("GitHubPluginConfig").Single().Element("Id")!.Value
        );
        Assert.Equal("123456", profile.Descendants("unsignedLong").Single().Value);
    }

    [Fact]
    public async Task Migration_moves_profiles_and_dev_manifests_not_caches()
    {
        string state = LegacySettings();
        File.SetUnixFileMode(state, (UnixFileMode)448);
        UserFile("Interim", "old wrapper");
        UserFile("Bin/Interim", "old binary");
        options.Settings = state;
        await installer.Run("migrate");
        AssertMigrated(state);
        Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(options.Target + "/Legacy"));
        Assert.False(File.Exists(options.Target + "/Interim"));
        Assert.False(Directory.Exists(options.Target + "/Bin"));
    }

    [Fact]
    public void Symlinked_config_trees_do_not_modify_originals()
    {
        string state = LegacySettings(),
            original = home + "/original-profiles";
        Directory.Move(state + "/Profiles", original);
        Directory.CreateSymbolicLink(state + "/Profiles", original);
        string before = File.ReadAllText(original + "/Current.xml"),
            copy = home + "/copied";
        Installer.CopySettings(state, copy);
        Installer.MigrateSettings(copy, log.Add);
        Assert.Equal(before, File.ReadAllText(original + "/Current.xml"));
        Assert.Null(new DirectoryInfo(copy + "/Profiles").LinkTarget);
    }

    [Fact]
    public async Task Migration_conflict_preserves_both_installations()
    {
        string state = LegacySettings(),
            old = home + "/old";
        Files.Write(old + "/Interim", [1]);
        Files.Write(old + "/Bin/Interim", [2]);
        await installer.Run("install");
        UserFile("Legacy/Profiles/Current.xml", "existing");
        options.Source = old;
        options.Settings = state;
        await Assert.ThrowsAsync<SetupError>(() => installer.Run("migrate"));
        Assert.Equal("existing", File.ReadAllText(options.Target + "/Legacy/Profiles/Current.xml"));
        Assert.True(Installer.Legacy(old));
    }

    [Fact]
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

    [Fact]
    public async Task Actual_1_0_16_install_migrate_update_uninstall()
    {
        string? bundle = Environment.GetEnvironmentVariable("PULSAR_TEST_LEGACY_BUNDLE");
        string? release = Environment.GetEnvironmentVariable("PULSAR_TEST_RELEASE_ARCHIVE");
        if (bundle is null || release is null)
            return; // Optional real-release fixture; see Docs/PulsarConfig.md.
        async Task OldScript(string action)
        {
            var start = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.Combine(bundle, action + ".sh"));
            start.Environment["PULSAR_DATA_DIR"] = options.Target;
            start.Environment["PULSAR_DIR"] = Files.OldConfig;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await errors);
        }
        await OldScript("install");
        string state = LegacySettings();
        options.Settings = state;
        options.Archive = release;
        options.Sha256 = Files.Hash(release);
        await installer.Run("migrate");
        AssertMigrated(state);
        Assert.Empty(
            Directory.GetFiles(Files.DataHome + "/icons", "pulsar.png", SearchOption.AllDirectories)
        );
        await installer.Run("update");
        await installer.Run("uninstall");
        Assert.True(File.Exists(options.Target + "/Legacy/Profiles/Current.xml"));
        await OldScript("uninstall");
        Assert.False(Directory.Exists(options.Target));
        Assert.False(File.Exists(installer.Desktop));
    }
}
