using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Pulsar.Config;
using Xunit;

namespace PulsarConfigTests;

public sealed class PortableInstallTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pulsar-portable-" + Guid.NewGuid().ToString("N"));

    public PortableInstallTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Package(string revision)
    {
        string path = Path.Combine(root, revision + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz"));
        var names = Installer.Required.Concat(Installer.Se2Required).Distinct();
        if (OperatingSystem.IsWindows())
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (string name in names)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(revision);
            }
        }
        else
        {
            using var gzip = new GZipStream(File.Create(path), CompressionMode.Compress);
            using var tar = new TarWriter(gzip);
            foreach (string name in names)
            {
                using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(revision));
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = bytes });
            }
        }
        return path;
    }

    [Fact]
    public async Task Native_install_update_rollback_and_uninstall_preserve_configuration()
    {
        var options = new Options { Target = Path.Combine(root, "Pulsar with spaces"), Game = "se2", Archive = Package("first") };
        var installer = new Installer(options, _ => { }, Path.Combine(root, "state"), Path.Combine(root, "shortcut"));
        await installer.Run("install");
        string exe = Path.Combine(options.Target, Installer.Launcher("se2"));
        Assert.Equal("first", File.ReadAllText(exe));
        Assert.EndsWith(" %command%", installer.LaunchOptions);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal($"\"{exe}\" %command%", installer.LaunchOptions);
            Assert.True(File.Exists(Path.Combine(options.Target, "Legacy.exe")));
            Assert.Contains("steam://rungameid/1133870", File.ReadAllText(installer.Desktop));
        }
        var editor = new PluginEditor(options);
        editor.PutSource(editor.SourcesList().Single(), editor.SourcesList().Single().Data);
        editor.SaveProfile("Saved profile");
        byte[] config = File.ReadAllBytes(editor.SourcesPath);
        options.Archive = Package("second");
        installer.Write = (path, bytes) => throw new IOException("Injected receipt write failure");
        await Assert.ThrowsAsync<IOException>(() => installer.Run("update"));
        Assert.Equal("first", File.ReadAllText(exe));
        Assert.Equal(config, File.ReadAllBytes(editor.SourcesPath));
        installer.Write = Files.Write;
        await installer.Run("update");
        Assert.Equal("second", File.ReadAllText(exe));
        Assert.Equal(config, File.ReadAllBytes(editor.SourcesPath));
        Assert.True(File.Exists(Path.Combine(editor.ProfilesDir, "Saved profile.xml")));
        await installer.Run("uninstall");
        Assert.False(File.Exists(exe));
        Assert.False(File.Exists(installer.Desktop));
        Assert.Equal(config, File.ReadAllBytes(editor.SourcesPath));
    }

    [WindowsFact]
    public void Windows_guard_detects_a_running_launcher()
    {
        string target = Path.Combine(root, "running");
        Directory.CreateDirectory(target);
        string exe = Path.Combine(target, "Interim.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-t 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            Assert.False(process.WaitForExit(300));
            Assert.Throws<SetupError>(() => Files.RequireStopped(target));
        }
        finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
    }

    [Theory]
    [InlineData("Libraries/../escape")]
    [InlineData("Libraries\\..\\escape")]
    [InlineData("Libraries/file.dll:stream")]
    [InlineData("Libraries/CON.dll")]
    [InlineData("Libraries/file. ")]
    [InlineData("/Libraries/file")]
    [InlineData("C:/Libraries/file")]
    public void Windows_archive_rejects_unsafe_paths(string name)
    {
        string archive = Path.Combine(root, "bad.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            zip.CreateEntry(name);
        Assert.Throws<SetupError>(() => Installer.UnpackZip(archive, Path.Combine(root, "stage")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_archive_rejects_links_and_case_collisions(bool link)
    {
        string archive = Path.Combine(root, "bad.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var first = zip.CreateEntry("Libraries/file.dll");
            if (link) first.ExternalAttributes = 0xA000 << 16;
            else zip.CreateEntry("Libraries/FILE.dll");
        }
        Assert.Throws<SetupError>(() => Installer.UnpackZip(archive, Path.Combine(root, "stage")));
    }

    [Fact]
    public void Windows_zip_supports_the_upstream_bundle_layout()
    {
        string archive = Environment.GetEnvironmentVariable("PULSAR_WINDOWS_ARCHIVE") ?? Path.Combine(root, "package.zip");
        if (!File.Exists(archive))
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (string name in new[] { "Interim.exe", "Legacy.exe", "Legacy.exe.config", "Modern.exe", "Libraries/Interim/Pulsar.Shared.dll" })
                zip.CreateEntry(name);
        }
        string destination = Path.Combine(root, "stage");
        Installer.UnpackZip(archive, destination);
        Assert.True(File.Exists(Path.Combine(destination, "Interim.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "Legacy.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "Modern.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "Libraries", "Interim", "Pulsar.Shared.dll")));
    }
}
