#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using CometWorks.ConfigTools;
using Terminal.Gui;
using Xunit;

namespace ConfigTools.Tests;

[Collection("ui-single-threaded")]
public sealed class InstallationCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "install-discovery-" + Guid.NewGuid().ToString("N"));
    private InstallationCatalog Catalog(params string[] paths) => new("Test", Path.Combine(root, "receipts"),
        path => new(path, File.Exists(Path.Combine(path, "launcher")) ? InstallationKind.Current : InstallationKind.Unknown),
        paths, Path.Combine(root, "history.json"));

    public InstallationCatalogTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Install(string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "launcher"), "program");
        return path;
    }
    private void Receipt(string path, bool installed)
    {
        string key = InstallationCatalog.Normalize(path);
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20];
        Directory.CreateDirectory(Path.Combine(root, "receipts"));
        File.WriteAllText(Path.Combine(root, "receipts", hash + ".json"), JsonSerializer.Serialize(new { target = path, installed }));
    }

    [Fact]
    public void Unpacked_installs_are_discovered_without_writes_and_remembered_separately()
    {
        Assert.Empty(Catalog(Path.Combine(root, "not-installed")).Discover());
        string first = Install("first"), second = Install("second");
        var catalog = Catalog(first, first + Path.DirectorySeparatorChar, second);
        Assert.Equal(2, catalog.Discover().Count);
        Assert.False(File.Exists(catalog.HistoryPath));
        Assert.False(Directory.Exists(catalog.ReceiptDirectory));
        catalog.Remember(second);
        Assert.Equal(second, Catalog(first).Discover()[0].Path);
        catalog.Forget(second);
        Assert.DoesNotContain(Catalog(first).Discover(), item => item.Path == second);
        Assert.True(File.Exists(Path.Combine(second, "launcher")));
        File.WriteAllText(catalog.HistoryPath, "not json");
        Assert.Single(Catalog(first).Discover());
        Assert.Equal("program", File.ReadAllText(Path.Combine(second, "launcher")));
    }

    [Fact]
    public void Startup_uses_one_valid_saved_location_without_selecting_unconfigured_discoveries()
    {
        string first = Install("first"), second = Install("second");
        var catalog = Catalog(first, second);
        Receipt(first, true);
        Assert.Null(catalog.SingleSavedInstallation());

        catalog.Remember(first);
        Assert.Equal(first, Catalog(second).SingleSavedInstallation()?.Path);
        byte[] history = File.ReadAllBytes(catalog.HistoryPath);
        Assert.Equal(first, catalog.SingleSavedInstallation()?.Path);
        Assert.Equal(history, File.ReadAllBytes(catalog.HistoryPath));

        catalog.Remember(second);
        Assert.Null(catalog.SingleSavedInstallation());
        File.Delete(Path.Combine(second, "launcher"));
        Assert.Equal(first, catalog.SingleSavedInstallation()?.Path);
        Directory.Delete(first, true);
        Assert.Null(catalog.SingleSavedInstallation());
        File.WriteAllText(catalog.HistoryPath, "not json");
        Assert.Null(catalog.SingleSavedInstallation());
    }

    [Fact]
    public void Receipts_are_hints_not_proof_and_uninstall_history_is_not_installed()
    {
        string existing = Install("external");
        string removed = Path.Combine(root, "removed");
        Directory.CreateDirectory(removed);
        File.WriteAllText(Path.Combine(removed, "settings.xml"), "keep");
        Receipt(existing, true);
        Receipt(removed, false);
        File.WriteAllText(Path.Combine(root, "receipts", "corrupt.json"), "{");
        File.WriteAllText(Path.Combine(root, "receipts", "wrong-shape.json"), "[]");
        var catalog = Catalog();
        var found = catalog.Discover();
        Assert.Equal(2, found.Count);
        Assert.True(found.Single(item => item.Path == existing).CanOpen);
        var retained = found.Single(item => item.Path == removed);
        Assert.Equal(InstallationKind.Retained, retained.Kind);
        Assert.True(retained.CanInstall);
        Assert.False(retained.CanUninstall);
        File.Delete(Path.Combine(existing, "launcher"));
        File.WriteAllText(Path.Combine(existing, "settings.xml"), "keep");
        Assert.Equal(InstallationKind.Incomplete, catalog.Inspect(existing).Kind);
        Directory.Delete(existing, true);
        Assert.Equal(InstallationKind.Missing, catalog.Discover().Single(item => item.Path == existing).Kind);
        string unrelated = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "notes.txt"), "keep");
        Assert.Equal(InstallationKind.Unknown, catalog.Inspect(unrelated).Kind);
        Assert.False(catalog.Inspect(unrelated).CanInstall);
    }

    [Fact]
    public void Search_is_bounded_cancellable_and_does_not_follow_directory_links()
    {
        string install = Install(Path.Combine("parent", "install"));
        var catalog = Catalog();
        Assert.Single(catalog.Search(root, CancellationToken.None, out _));
        Assert.Empty(catalog.Search(root, CancellationToken.None, out bool limited, maxDepth: 0));
        Assert.True(limited);
        Assert.Empty(catalog.Search(root, CancellationToken.None, out limited, maxDirectories: 1));
        Assert.True(limited);
        Assert.Throws<OperationCanceledException>(() => catalog.Search(root, new CancellationToken(true), out _));
        if (!OperatingSystem.IsWindows())
        {
            string alias = Path.Combine(root, "alias");
            Directory.CreateSymbolicLink(alias, install);
            Directory.CreateSymbolicLink(Path.Combine(root, "parent", "loop"), root);
            Assert.Single(Catalog(install, alias).Discover());
            Assert.Single(catalog.Search(root, CancellationToken.None, out _));
        }
    }

    [Theory]
    [InlineData(80, 24, false)]
    [InlineData(132, 40, false)]
    [InlineData(80, 24, true)]
    public void Setup_actions_fit_and_focused_fields_scroll_into_view(int width, int height, bool turbo)
    {
        var driver = new FakeDriver();
        var loop = (IMainLoopDriver)Activator.CreateInstance(typeof(FakeDriver).Assembly.GetType("Terminal.Gui.FakeMainLoop")!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { driver }, null)!;
        Application.Init(driver, loop);
        try
        {
            driver.SetBufferSize(width, height);
            TerminalTheme.Apply(turbo ? ThemeKind.Turbo : ThemeKind.Sandstone);
            using var pointer = new PointerHighlight();
            using var screen = new SetupWorkspace(Catalog(), Install("ui"), 14);
            var release = screen.Field(6, "Release", "latest");
            var last = screen.Field(12, "DS binaries", "some folder");
            var state = Application.Begin(screen.Dialog);
            try
            {
                screen.Dialog.LayoutSubviews();
                Assert.False(screen.Actions["install"].Enabled);
                Assert.True(screen.Actions["update"].Enabled);
                Assert.True(screen.Actions["uninstall"].Enabled);
                foreach (var button in screen.Actions.Values.Append(screen.Cancel).Append(screen.Back))
                {
                    Assert.Equal(3, button.Frame.Height);
                    Assert.True(button.Frame.X >= 0 && button.Frame.Right <= screen.Dialog.Bounds.Width);
                    Assert.True(button.Frame.Y >= screen.Fields.Frame.Bottom);
                    Assert.True(button.Frame.Bottom <= screen.Dialog.Bounds.Height);
                }
                last.SetFocus();
                int visibleTop = -screen.Fields.ContentOffset.Y;
                Assert.InRange(last.Frame.Y, visibleTop, visibleTop + screen.Fields.Bounds.Height - 1);
                screen.Target.SetFocus();
                Assert.Equal(0, screen.Fields.ContentOffset.Y);
                var tab = new KeyEvent(Key.Tab, new KeyModifiers());
                screen.Dialog.ProcessKey(tab);
                screen.Dialog.ProcessKey(tab);
                Assert.True(release.HasFocus);
                screen.Dialog.ProcessKey(tab);
                Assert.True(last.HasFocus);
                screen.Dialog.ProcessKey(tab);
                Assert.True(screen.Actions["update"].HasFocus);
                int clicks = 0;
                var update = screen.Actions["update"];
                update.Clicked += () => clicks++;
                for (int y = 0; y < 3; y++)
                    update.MouseEvent(new MouseEvent { X = 3, Y = y, Flags = MouseFlags.Button1Clicked, View = update });
                Assert.Equal(3, clicks);
                screen.SetBusy(true);
                Assert.False(screen.Target.Enabled);
                Assert.False(update.Enabled);
                Assert.True(screen.Cancel.Enabled);
                screen.SetBusy(false);
                Assert.True(update.Enabled);
                Assert.False(screen.Cancel.Enabled);
                Assert.False(screen.Actions["install"].Enabled);
            }
            finally { Application.End(state); }
        }
        finally { Application.Shutdown(); }
    }
}
