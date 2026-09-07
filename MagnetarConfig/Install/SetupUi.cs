#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Magnetar.Config.Install;

internal static class SetupUi
{
    public static void Run(string? target = null, string? ds64 = null)
        => Run(new InstallOptions { Target = target ?? InstallOptions.DefaultTarget(), Ds64 = ds64 });

    public static void Run(InstallOptions initial)
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
            using var dialog = new Dialog("Magnetar · Server setup")
                { Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = TerminalTheme.Window };
            TextField Field(int row, string label, string value)
            {
                dialog.Add(new Label(label) { X = 1, Y = row });
                var field = new TextField(value) { X = 16, Y = row, Width = Dim.Fill(2) };
                dialog.Add(field);
                return field;
            }
            var targetField = Field(1, "Installation", initial.Target);
            var version = Field(3, "Release", initial.Version);
            var archive = Field(5, "Local .7z", initial.Archive ?? "");
            var checksum = Field(7, "SHA-256", initial.Sha256 ?? "");
            var dedicated = Field(9, "DS binaries", initial.Ds64 ?? "");
            var dependencies = new Button("Check prerequisites") { X = 1, Y = 11 };
            dialog.Add(dependencies);
            var log = new TextView { X = 1, Y = 15, Width = Dim.Fill(2), Height = Dim.Fill(1), ReadOnly = true, WordWrap = true };
            dialog.Add(log);
            var lines = new List<string>();
            var controls = new List<View> { targetField, version, archive, checksum, dedicated, dependencies };
            bool busy = false;
            CancellationTokenSource? cancellation = null;
            void Append(string message)
            {
                lines.Add(message);
                if (lines.Count > 500) lines.RemoveAt(0);
                log.Text = string.Join('\n', lines);
                log.MoveEnd();
            }
            async void Start(string action)
            {
                if (busy) return;
                var options = new InstallOptions
                {
                    Target = targetField.Text.ToString() ?? "",
                    Version = (version.Text.ToString() ?? "").Trim(),
                    Archive = Empty(archive.Text.ToString()), Sha256 = Empty(checksum.Text.ToString()),
                    Ds64 = Empty(dedicated.Text.ToString()), CheckDependencies = initial.CheckDependencies,
                };
                string message = $"{action} Magnetar at:\n{options.Target}\n\nExisting files are backed up. Configurations, worlds, and unrelated files are preserved.";
                if (action != "uninstall") message += $"\n\nPackage: {options.Archive ?? options.Version}";
                if (action != "check" && MessageBox.Query("Confirm server setup", message, "Cancel", "Continue") != 1) return;
                busy = true;
                foreach (var control in controls) control.Enabled = false;
                cancellation = new CancellationTokenSource();
                CancellationToken token = cancellation.Token;
                Append($"Starting {action}…");
                try
                {
                    await Task.Run(() => new Installer(options, text => Application.MainLoop.Invoke(() => Append(text))).Run(action, token));
                    Application.MainLoop.Invoke(() => Append("Done."));
                }
                catch (OperationCanceledException) { Application.MainLoop.Invoke(() => Append("Setup cancelled before switching the installation.")); }
                catch (Exception error) { Application.MainLoop.Invoke(() => Append($"Setup stopped: {error.Message}")); }
                finally
                {
                    Application.MainLoop.Invoke(() =>
                    {
                        cancellation.Dispose();
                        cancellation = null;
                        busy = false;
                        foreach (var control in controls) control.Enabled = true;
                    });
                }
            }
            int column = 1;
            dependencies.Clicked += () => Start("check");
            foreach (string action in new[] { "install", "update", "uninstall" })
            {
                var button = new Button(char.ToUpperInvariant(action[0]) + action[1..]) { X = column, Y = 13 };
                button.Clicked += () => Start(action);
                dialog.Add(button);
                controls.Add(button);
                column += action.Length + 5;
            }
            var cancel = new Button("Cancel task") { X = column, Y = 13 };
            cancel.Clicked += () => cancellation?.Cancel();
            var close = new Button("Close") { X = Pos.Right(cancel) + 1, Y = 13 };
            close.Clicked += () => { if (!busy) Application.RequestStop(); };
            dialog.Add(cancel, close);
            dialog.Closing += args => { if (busy) args.Cancel = true; };
            Append("Install the current portable server package. Leave Local .7z empty to download a release.");
            Append("Updates and uninstall keep your server state. Stop every server using the installation first.");
            Application.Run(dialog);
        }
        finally { if (ownsApplication) Application.Shutdown(); }
    }

    private static string? Empty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
