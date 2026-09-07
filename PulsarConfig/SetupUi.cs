using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Pulsar.Config;

internal static class SetupUi
{
    public static void Run(Options options)
    {
        using var top = new WorkspaceDialog("Pulsar · Linux setup")
        {
            Width = Dim.Function(() => Math.Min(132, Application.Driver.Cols - 2)),
            Height = Dim.Function(() => Math.Min(36, Application.Driver.Rows - 2)),
            ColorScheme = TerminalTheme.Window,
        };
        {
            var window = top;
            TextField Field(int row, string label, string value)
            {
                window.Add(
                    new Label(label)
                    {
                        X = 3,
                        Y = row,
                        ColorScheme = TerminalTheme.Desktop,
                    }
                );
                var field = new TextField(value)
                {
                    X = 18,
                    Y = row,
                    Width = Dim.Fill(3),
                };
                window.Add(field);
                return field;
            }
            var target = Field(1, "Installation", options.Target);
            window.Add(
                new Label("Game")
                {
                    X = 3,
                    Y = 3,
                    ColorScheme = TerminalTheme.Desktop,
                }
            );
            var game = new RadioGroup(["Auto", "SE1", "SE2"])
            {
                X = 18,
                Y = 3,
                DisplayMode = DisplayModeLayout.Horizontal,
                SelectedItem = Array.IndexOf(new[] { "auto", "se1", "se2" }, options.Game),
            };
            window.Add(game);
            var version = Field(5, "Release", options.Version);
            var source = Field(7, "Old install", options.Source ?? "");
            var settings = Field(9, "Old settings", options.Settings ?? Files.OldConfig);
            window.Add(
                new Label(
                    "Game: auto / se1 / se2 · Old install/settings are used only for migration."
                )
                {
                    X = 3,
                    Y = 11,
                    Width = Dim.Fill(3),
                    ColorScheme = TerminalTheme.Desktop,
                }
            );
            var log = new TextView
            {
                X = 3,
                Y = 18,
                Width = Dim.Fill(3),
                Height = Dim.Fill(1),
                ReadOnly = true,
                WordWrap = true,
                ColorScheme = TerminalTheme.HomeAction,
            };
            window.Add(
                log,
                new Label("Activity")
                {
                    X = 3,
                    Y = 17,
                    ColorScheme = TerminalTheme.Title,
                },
                new LineView
                {
                    X = 3,
                    Y = 16,
                    Width = Dim.Fill(3),
                    ColorScheme = TerminalTheme.Border,
                }
            );
            var lines = new List<string>();
            bool busy = false,
                leaving = false;
            CancellationTokenSource? cancellation = null;
            var controls = new List<View> { target, game, version, source, settings };
            void Append(string message)
            {
                lines.Add(message);
                // Downloads are bounded, but keep the log bounded across repeated operations too.
                if (lines.Count > 500)
                    lines.RemoveAt(0);
                log.Text = string.Join('\n', lines);
                log.CursorPosition = new Point(0, Math.Max(0, log.Lines - 1));
            }
            WorkspaceButton? cancel = null;
            async void Start(string action)
            {
                if (busy)
                    return;
                options.Target = target.Text.ToString() ?? "";
                options.Game = new[] { "auto", "se1", "se2" }[game.SelectedItem];
                options.Version = (version.Text.ToString() ?? "").Trim();
                options.Source = string.IsNullOrWhiteSpace(source.Text.ToString())
                    ? null
                    : source.Text.ToString();
                options.Settings = settings.Text.ToString();
                if (options.Game is not ("auto" or "se1" or "se2"))
                {
                    MessageBox.ErrorQuery("Game selection", "Enter auto, se1 or se2.", "OK");
                    return;
                }
                string message =
                    $"{action} Pulsar at:\n{options.Target}\n\nExisting installations are backed up. Profiles and local plugins are preserved.";
                if (action == "migrate")
                    message +=
                        $"\n\nOld install: {options.Source ?? options.Target}\nOld settings: {options.Settings}";
                if (action != "uninstall")
                    message += $"\n\nPackage: {options.Archive ?? options.Version}";
                if (
                    action != "check"
                    && MessageBox.Query("Confirm setup", message, "Cancel", "Continue") != 1
                )
                    return;
                busy = true;
                cancel!.Enabled = true;
                foreach (var control in controls)
                    control.Enabled = false;
                cancellation = new CancellationTokenSource();
                Append($"Starting {action}…");
                try
                {
                    await Task.Run(async () =>
                    {
                        void Report(string text) => Application.MainLoop.Invoke(() => Append(text));
                        if (action == "check")
                            Report(new Installer(options, Report).CheckPrerequisites());
                        else
                            await new Installer(options, Report).Run(action, cancellation.Token);
                    });
                    Application.MainLoop.Invoke(() => Append("Done."));
                }
                catch (OperationCanceledException)
                {
                    Application.MainLoop.Invoke(() => Append("Setup cancelled."));
                }
                catch (Exception error)
                {
                    Application.MainLoop.Invoke(() => Append($"Setup stopped: {error.Message}"));
                }
                finally
                {
                    Application.MainLoop.Invoke(() =>
                    {
                        busy = false;
                        cancel!.Enabled = false;
                        cancellation.Dispose();
                        cancellation = null;
                        foreach (var control in controls)
                            control.Enabled = true;
                        if (leaving)
                            Application.RequestStop();
                    });
                }
            }
            int column = 3;
            foreach (string action in new[] { "install", "update", "migrate", "uninstall" })
            {
                var button = new WorkspaceButton(char.ToUpperInvariant(action[0]) + action[1..])
                {
                    X = column,
                    Y = 13,
                    Height = 3,
                };
                button.Clicked += () => Start(action);
                window.Add(button);
                controls.Add(button);
                column += action.Length + 6;
            }
            var check = new WorkspaceButton("Check prerequisites") { X = 3, Y = 12 };
            check.Clicked += () => Start("check");
            window.Add(check);
            controls.Add(check);
            cancel = new WorkspaceButton("Cancel task")
            {
                X = Pos.Right(check) + 2,
                Y = 12,
                Enabled = false,
            };
            cancel.Clicked += () => cancellation?.Cancel();
            window.Add(cancel);
            var quit = new WorkspaceButton("Back")
            {
                X = column,
                Y = 13,
                Height = 3,
            };
            quit.Clicked += () => Application.RequestStop();
            window.Add(quit);
            top.Closing += args =>
            {
                if (!busy)
                    return;
                args.Cancel = true;
                if (leaving)
                    return;
                leaving = true;
                Append("Cancelling setup before leaving…");
                cancellation?.Cancel();
            };
            Append(
                "Install or update from SpaceGT/Pulsar releases. Steam launch options will be shown here."
            );
            Application.Run(top);
        }
    }
}
