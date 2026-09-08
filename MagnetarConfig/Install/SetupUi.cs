#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Magnetar.Config.Install;

internal static class SetupUi
{
    public static void Run(string? target = null, string? ds64 = null)
        => Run(new InstallOptions { Target = target ?? InstallOptions.DefaultTarget(), Ds64 = ds64 });

    public static void Run(InstallOptions initial, bool checkUpdates = false)
    {
        bool ownsApplication = Application.Driver is null;
        using var palette = ownsApplication ? PaletteConsole.Attach() : null;
        if (ownsApplication)
        {
            Application.UseSystemConsole = true;
            Application.Init();
            TerminalTheme.Apply(ThemePreference.Load(ThemePreference.FilePath, ThemeKind.Sandstone));
        }
        try
        {
            using var pointer = new PointerHighlight();
            using var screen = new SetupWorkspace(InstallationDiscovery.Create(), initial.Target, 14);
            var version = screen.Field(6, "Release", initial.Version);
            var archive = screen.Field(8, "Local .7z", initial.Archive ?? "");
            var checksum = screen.Field(10, "SHA-256", initial.Sha256 ?? "");
            var dedicated = screen.Field(12, "DS binaries", initial.Ds64 ?? "");
            bool busy = false, leaving = false;
            using var startupUpdate = checkUpdates || ownsApplication
                ? new StartupUpdateCheck(screen.Dialog, release =>
                {
                    if (SelfUpdateUi.Show(release)) Application.RequestStop(screen.Dialog);
                }, canPrompt: () => !busy) : null;
            CancellationTokenSource? cancellation = null;
            async void Start(string action)
            {
                if (busy) return;
                var options = new InstallOptions
                {
                    Target = screen.Target.Text.ToString() ?? "",
                    Version = (version.Text.ToString() ?? "").Trim(),
                    Archive = Empty(archive.Text.ToString()), Sha256 = Empty(checksum.Text.ToString()),
                    Ds64 = Empty(dedicated.Text.ToString()), CheckDependencies = initial.CheckDependencies,
                };
                string message = $"{action} Magnetar at:\n{options.Target}\n\nExisting files are backed up. Configurations, worlds, and unrelated files are preserved.";
                if (action != "uninstall") message += $"\n\nPackage: {options.Archive ?? options.Version}";
                if (action != "check" && MessageBox.Query("Confirm server setup", message, "Cancel", "Continue") != 1) return;
                busy = true;
                screen.SetBusy(true);
                cancellation = new CancellationTokenSource();
                var token = cancellation.Token;
                screen.Append($"Starting {action}…");
                try
                {
                    await Task.Run(() => new Installer(options, text => Application.MainLoop.Invoke(() => screen.Append(text))).Run(action, token));
                    Application.MainLoop.Invoke(() => { screen.Append("Done."); screen.Remember(); });
                }
                catch (OperationCanceledException) { Application.MainLoop.Invoke(() => screen.Append("Setup cancelled before switching the installation.")); }
                catch (Exception error) { Application.MainLoop.Invoke(() => screen.Append("Setup stopped: " + error.Message)); }
                finally
                {
                    Application.MainLoop.Invoke(() =>
                    {
                        cancellation.Dispose();
                        cancellation = null;
                        busy = false;
                        screen.SetBusy(false);
                        if (leaving) Application.RequestStop(screen.Dialog);
                    });
                }
            }
            foreach (var (action, button) in screen.Actions) button.Clicked += () => Start(action);
            screen.Cancel.Clicked += () => cancellation?.Cancel();
            screen.Back.Clicked += () => Application.RequestStop(screen.Dialog);
            screen.Dialog.Closing += args =>
            {
                if (!busy) return;
                args.Cancel = true;
                leaving = true;
                cancellation?.Cancel();
            };
            screen.Append("Choose an existing installation or enter a new folder. Leave Local .7z empty to download a release.");
            screen.Append("Updates and uninstall keep server state. Stop servers using this installation first.");
            Application.Run(screen.Dialog);
            initial.Target = screen.Target.Text.ToString() ?? initial.Target;
        }
        finally { if (ownsApplication) Application.Shutdown(); }
    }

    private static string? Empty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
