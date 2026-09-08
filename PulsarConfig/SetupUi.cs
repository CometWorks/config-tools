using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Pulsar.Config;

internal static class SetupUi
{
    public static void Run(Options options)
    {
        using var screen = new SetupWorkspace(InstallationDiscovery.Create(), options.Target, 10);
        screen.Fields.Add(new Label("Game") { X = 0, Y = 6, ColorScheme = TerminalTheme.Desktop });
        var game = new RadioGroup(["Auto", "SE1", "SE2"])
        {
            X = 16, Y = 6, DisplayMode = DisplayModeLayout.Horizontal,
            SelectedItem = Array.IndexOf(new[] { "auto", "se1", "se2" }, options.Game),
        };
        screen.Fields.Add(game);
        screen.Controls.Add(game);
        screen.TrackFocus(game);
        var version = screen.Field(8, "Release", options.Version);
        bool busy = false, leaving = false;
        CancellationTokenSource? cancellation = null;
        async void Start(string action)
        {
            if (busy) return;
            options.Target = screen.Target.Text.ToString() ?? "";
            options.Game = new[] { "auto", "se1", "se2" }[game.SelectedItem];
            options.Version = (version.Text.ToString() ?? "").Trim();
            string message = $"{action} Pulsar at:\n{options.Target}\n\nExisting installations are backed up. Profiles and local plugins are preserved.";
            if (action != "uninstall") message += $"\n\nPackage: {options.Archive ?? options.Version}";
            if (action != "check" && MessageBox.Query("Confirm setup", message, "Cancel", "Continue") != 1) return;
            busy = true;
            screen.SetBusy(true);
            cancellation = new CancellationTokenSource();
            screen.Append($"Starting {action}…");
            try
            {
                var token = cancellation.Token;
                await Task.Run(async () =>
                {
                    void Report(string text) => Application.MainLoop.Invoke(() => screen.Append(text));
                    if (action == "check") Report(new Installer(options, Report).CheckPrerequisites());
                    else await new Installer(options, Report).Run(action, token);
                });
                Application.MainLoop.Invoke(() => { screen.Append("Done."); screen.Remember(); });
            }
            catch (OperationCanceledException) { Application.MainLoop.Invoke(() => screen.Append("Setup cancelled.")); }
            catch (Exception error) { Application.MainLoop.Invoke(() => screen.Append("Setup stopped: " + error.Message)); }
            finally
            {
                Application.MainLoop.Invoke(() =>
                {
                    busy = false;
                    cancellation.Dispose();
                    cancellation = null;
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
        screen.Append("Select an existing installation or enter a new folder. Steam launch options appear here after setup.");
        Application.Run(screen.Dialog);
        options.Target = screen.Target.Text.ToString() ?? options.Target;
        options.Game = new[] { "auto", "se1", "se2" }[game.SelectedItem];
    }
}
