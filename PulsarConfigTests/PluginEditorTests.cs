using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Pulsar.Config;
using Xunit;

namespace PulsarConfigTests;

[SupportedOSPlatform("linux")]
public sealed class PluginEditorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "pulsar-editor-" + Guid.NewGuid().ToString("N")
    );
    private readonly string? oldState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
    internal readonly Options Options;
    internal readonly PluginEditor Editor;

    public PluginEditorTests()
    {
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", root + "/state");
        Options = new Options { Target = root + "/install", Game = "se1" };
        foreach (string file in Installer.Required.Concat(Installer.Se2Required))
        {
            string path = Path.Combine(Options.Target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture");
        }
        Editor = new PluginEditor(Options);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", oldState);
        Directory.Delete(root, true);
    }

    private string Manifest(string folder)
    {
        string path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(
            path + "/Remote.xml",
            "<PluginData><Id>different-manifest-id</Id><FriendlyName>Remote</FriendlyName></PluginData>"
        );
        return path;
    }

    private static XElement Dev(string folder) =>
        new(
            "LocalPlugin",
            new XElement("Name", "Remote"),
            new XElement("Folder", folder),
            new XElement("File", "Remote.xml"),
            new XElement("Enabled", true)
        );

    [Fact]
    public void Dev_registration_matches_loader_identity_and_renames_saved_profile_references()
    {
        string folder = Manifest("se-remote");
        Editor.PutSource(null, Dev(folder), active: true, debug: false);
        var sources = XElement.Load(Editor.SourcesPath);
        Assert.Equal(
            "Remote.xml",
            sources.Element("LocalPluginSources")!.Element("LocalPlugin")!.Element("File")!.Value
        );
        var current = XElement.Load(Editor.CurrentPath);
        Assert.Equal(
            "se-remote",
            current.Element("DevFolder")!.Elements().Single().Element("Id")!.Value
        );
        Assert.Equal(
            "false",
            current.Element("DevFolder")!.Elements().Single().Element("DebugBuild")!.Value
        );
        Assert.Equal(
            "StarCpt/PluginHub",
            sources.Element("RemoteHubSources")!.Elements().Single().Element("Repo")!.Value
        );
        Editor.SaveProfile("Development");
        var original = Editor.SourcesList().Single(s => s.Kind == "LocalPlugin");
        Editor.PutSource(original, Dev(Manifest("se-remote-next")), active: true, debug: true);
        Assert.Equal(
            "se-remote-next",
            XElement
                .Load(Editor.ProfilesDir + "/Development.xml")
                .Element("DevFolder")!
                .Elements()
                .Single()
                .Element("Id")!
                .Value
        );
        Assert.Equal((true, true), Editor.DevState("se-remote-next"));
        Assert.True(File.Exists(Editor.CurrentPath + ".bak"));
    }

    [Fact]
    public void Profiles_preserve_pins_mods_unknown_fields_and_private_permissions()
    {
        Directory.CreateDirectory(Editor.ProfilesDir);
        string original =
            "<Profile><Name>Current</Name><GitHub><GitHubPluginConfig><Id>plugin</Id><SelectedVersion>pinned</SelectedVersion><Future>keep</Future></GitHubPluginConfig></GitHub><DevFolder/><Local/><Mods><unsignedLong>123456</unsignedLong></Mods><Unknown>preserve</Unknown></Profile>";
        File.WriteAllText(Editor.CurrentPath, original);
        File.SetUnixFileMode(Editor.CurrentPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Editor.SaveProfile("With mods");
        var plugin = Editor.Plugins().Single(p => p.Id == "plugin");
        Editor.TogglePlugin(plugin);
        Editor.LoadProfile("With mods");
        var current = XElement.Load(Editor.CurrentPath);
        Assert.Equal(
            "pinned",
            current.Element("GitHub")!.Elements().Single().Element("SelectedVersion")!.Value
        );
        Assert.Equal("123456", current.Element("Mods")!.Elements().Single().Value);
        Assert.Equal("preserve", current.Element("Unknown")!.Value);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Editor.CurrentPath)
        );
        Assert.Equal(original, File.ReadAllText(Editor.CurrentPath + ".bak"));
        Editor.SaveProfile("Renamed", "With mods");
        Assert.False(File.Exists(Editor.ProfilesDir + "/With mods.xml"));
        Editor.DeleteProfile("Renamed");
        Assert.Empty(Editor.Profiles());
        Assert.Throws<SetupError>(() => Editor.SaveProfile("../escape"));
        Assert.Throws<SetupError>(() => Editor.DeleteProfile("Current"));
        Assert.Throws<SetupError>(() => Editor.LoadProfile("deleted"));
        Assert.Equal(
            "pinned",
            XElement
                .Load(Editor.CurrentPath)
                .Element("GitHub")!
                .Elements()
                .Single()
                .Element("SelectedVersion")!
                .Value
        );
    }

    [Fact]
    public void Source_edits_preserve_unknown_fields_reset_cache_and_reject_stale_edits()
    {
        var hub = Editor.SourcesList().Single();
        var first = new XElement(hub.Data);
        first.SetElementValue("Hash", "old");
        first.SetElementValue("Custom", "keep");
        first.SetElementValue("CustomHash", "also keep");
        Editor.PutSource(hub, first);
        var source = Editor.SourcesList().Single();
        var changed = new XElement(source.Data);
        changed.SetElementValue("Branch", "development");
        Editor.PutSource(source, changed);
        Assert.Null(XElement.Load(Editor.SourcesPath).Descendants("Hash").FirstOrDefault());
        Assert.Equal(
            "keep",
            XElement.Load(Editor.SourcesPath).Descendants("Custom").Single().Value
        );
        Assert.Equal(
            "also keep",
            XElement.Load(Editor.SourcesPath).Descendants("CustomHash").Single().Value
        );
        Assert.Throws<SetupError>(() => Editor.PutSource(source, source.Data));
    }

    [Fact]
    public void Failed_profile_write_rolls_back_dev_registration()
    {
        Editor.PutSource(Editor.SourcesList().Single(), Editor.SourcesList().Single().Data);
        string before = File.ReadAllText(Editor.SourcesPath);
        Directory.CreateDirectory(Editor.ProfilesDir);
        File.SetUnixFileMode(Editor.ProfilesDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() =>
                Editor.PutSource(null, Dev(Manifest("dev")), true)
            );
            Assert.Equal(before, File.ReadAllText(Editor.SourcesPath));
            Assert.False(File.Exists(Editor.CurrentPath));
        }
        finally
        {
            File.SetUnixFileMode(
                Editor.ProfilesDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    [Fact]
    public void SE2_uses_Modern_and_Steam_without_changing_launch_options()
    {
        var editor = new PluginEditor(new Options { Target = Options.Target, Game = "se2" });
        Assert.EndsWith("/Modern", editor.ConfigDir);
        Assert.Equal("StarCpt/PluginHub-SE2", editor.SourcesList().Single().Key);
        var launch = editor.LaunchCommand();
        Assert.Equal("steam", launch.FileName);
        Assert.Equal(new[] { "-applaunch", "1133870" }, launch.ArgumentList);
        Assert.Equal(new[] { "-applaunch", "244850" }, Editor.LaunchCommand().ArgumentList);
        Assert.False(File.Exists(editor.SourcesPath));
    }

    [Fact]
    public void Malformed_configuration_is_never_overwritten()
    {
        Directory.CreateDirectory(Editor.ProfilesDir);
        File.WriteAllText(Editor.CurrentPath, "<Broken>");
        Assert.ThrowsAny<Exception>(() => Editor.SaveProfile("new"));
        Assert.Equal("<Broken>", File.ReadAllText(Editor.CurrentPath));
        Assert.False(File.Exists(Editor.ProfilesDir + "/new.xml"));
    }

    [Fact]
    public void Nested_local_plugin_bundle_uses_parent_folder_id()
    {
        Directory.CreateDirectory(Editor.ConfigDir + "/Local/my-plugin");
        File.WriteAllText(Editor.ConfigDir + "/Local/my-plugin/plugin.dll", "fixture");
        var plugin = Editor.Plugins().Single();
        Assert.Equal("my-plugin.dll", plugin.Id);
        Editor.TogglePlugin(plugin);
        Assert.Equal(
            "my-plugin.dll",
            XElement.Load(Editor.CurrentPath).Element("Local")!.Elements().Single().Value
        );
    }

    [Fact]
    public void Local_hub_dependencies_include_hidden_plugins_and_fail_without_partial_writes()
    {
        string hub = root + "/hub";
        Directory.CreateDirectory(hub);
        File.WriteAllText(
            hub + "/first.xml",
            "<PluginData xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:type=\"GitHubPlugin\"><Id>first</Id><FriendlyName>First</FriendlyName><DependencyIds><Id>hidden</Id></DependencyIds></PluginData>"
        );
        File.WriteAllText(
            hub + "/hidden.xml",
            "<PluginData xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:type=\"GitHubPlugin\"><Id>hidden</Id><Hidden>true</Hidden><DependencyIds><Id>first</Id></DependencyIds></PluginData>"
        );
        Editor.PutSource(
            null,
            new XElement(
                "LocalHub",
                new XElement("Name", "Local"),
                new XElement("Folder", hub),
                new XElement("Enabled", true)
            )
        );
        Assert.Single(Editor.Plugins());
        Editor.TogglePlugin(Editor.Plugins().Single());
        Assert.Equal(
            new[] { "first", "hidden" },
            XElement
                .Load(Editor.CurrentPath)
                .Element("GitHub")!
                .Elements()
                .Select(e => e.Element("Id")!.Value)
        );
        Editor.TogglePlugin(Editor.Plugins().Single(p => p.Id == "first"));
        File.Delete(hub + "/hidden.xml");
        // An enabled missing dependency is retained; an entirely unresolved one aborts.
        File.WriteAllText(
            hub + "/first.xml",
            File.ReadAllText(hub + "/first.xml").Replace("<Id>hidden</Id>", "<Id>missing</Id>")
        );
        string before = File.ReadAllText(Editor.CurrentPath);
        Assert.Throws<SetupError>(() =>
            Editor.TogglePlugin(Editor.Plugins().Single(p => p.Id == "first"))
        );
        Assert.Equal(before, File.ReadAllText(Editor.CurrentPath));
    }

    [Fact]
    public void Written_profiles_and_sources_load_with_the_real_Pulsar_serializer()
    {
        string? sharedPath = Environment.GetEnvironmentVariable("PULSAR_SHARED");
        if (sharedPath is null)
            return; // Optional installed-runtime interoperability check.
        Editor.PutSource(null, Dev(Manifest("se-remote")), true, false);
        Editor.SaveProfile("Development");
        ResolveEventHandler resolver = (_, args) =>
        {
            string path = Path.Combine(
                Path.GetDirectoryName(sharedPath)!,
                new AssemblyName(args.Name).Name + ".dll"
            );
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            var shared = Assembly.LoadFrom(sharedPath);
            Type profileType = shared.GetType("Pulsar.Shared.Data.Profile")!;
            using var reader = File.OpenRead(Editor.CurrentPath);
            var profile = new System.Xml.Serialization.XmlSerializer(profileType).Deserialize(
                reader
            )!;
            Assert.True((bool)profileType.GetMethod("Validate")!.Invoke(profile, null)!);
            var devs = (
                (System.Collections.IEnumerable)
                    profileType.GetProperty("DevFolder")!.GetValue(profile)!
            ).Cast<object>();
            object dev = Assert.Single(devs);
            Assert.Equal("se-remote", dev.GetType().GetProperty("Id")!.GetValue(dev));
            Assert.Equal(false, dev.GetType().GetProperty("DebugBuild")!.GetValue(dev));
            Type sourcesType = shared.GetType("Pulsar.Shared.Config.SourcesConfig")!;
            using var sourceReader = File.OpenRead(Editor.SourcesPath);
            object sources = new System.Xml.Serialization.XmlSerializer(sourcesType).Deserialize(
                sourceReader
            )!;
            var entries = (
                (System.Collections.IEnumerable)
                    sourcesType.GetProperty("LocalPluginSources")!.GetValue(sources)!
            ).Cast<object>();
            object entry = Assert.Single(entries);
            Assert.Equal("Remote.xml", entry.GetType().GetProperty("File")!.GetValue(entry));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    [Fact]
    public void Editing_refuses_a_running_Pulsar_process()
    {
        string exe = Options.Target + "/Interim.bin";
        File.Copy("/bin/sleep", exe, true);
        File.SetUnixFileMode(
            exe,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        using var process = System.Diagnostics.Process.Start(exe, "30")!;
        try
        {
            Assert.Throws<SetupError>(() => Editor.SaveProfile("while running"));
            Assert.False(File.Exists(Editor.ProfilesDir + "/while running.xml"));
        }
        finally
        {
            process.Kill();
            process.WaitForExit();
        }
    }

    [Fact]
    public void Full_shell_navigates_all_editors_without_writing_config()
    {
        var driver = new Terminal.Gui.FakeDriver();
        Type type = typeof(Terminal.Gui.FakeDriver).Assembly.GetType("Terminal.Gui.FakeMainLoop")!;
        var loop = (Terminal.Gui.IMainLoopDriver)
            Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { driver },
                null
            )!;
        Terminal.Gui.Application.Init(driver, loop);
        try
        {
            CometWorks.ConfigTools.TerminalTheme.Apply();
            using var shell = new ConfigShell(Options);
            var state = Terminal.Gui.Application.Begin(shell);
            foreach (
                Action navigate in new Action[]
                {
                    shell.Plugins,
                    shell.DevFolders,
                    shell.Sources,
                    shell.Profiles,
                    shell.Dashboard,
                }
            )
            {
                navigate();
                bool wait = false;
                Terminal.Gui.Application.RunMainLoopIteration(ref state, false, ref wait);
            }
            Terminal.Gui.Application.End(state);
            Assert.False(File.Exists(Editor.SourcesPath));
            Assert.False(File.Exists(Editor.CurrentPath));
        }
        finally
        {
            Terminal.Gui.Application.Shutdown();
        }
    }
}
