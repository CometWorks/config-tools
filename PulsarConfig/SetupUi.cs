using CometWorks.ConfigTools;
using Terminal.Gui;

namespace Pulsar.Config;

internal static class SetupUi
{
    public static void Run(Options options)
    {
        using var top = new Toplevel();
        {
            var window = new Window("Pulsar · Linux setup")
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
            };
            top.Add(window);
            TextField Field(int row, string label, string value)
            {
                window.Add(new Label(label) { X = 1, Y = row });
                var field = new TextField(value)
                {
                    X = 16,
                    Y = row,
                    Width = Dim.Fill(2),
                };
                window.Add(field);
                return field;
            }
            var target = Field(1, "Installation", options.Target);
            window.Add(new Label("Game") { X = 1, Y = 3 });
            var game = new RadioGroup(["Auto", "SE1", "SE2"])
            {
                X = 16,
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
                    X = 1,
                    Y = 11,
                }
            );
            var log = new TextView
            {
                X = 1,
                Y = 15,
                Width = Dim.Fill(2),
                Height = Dim.Fill(1),
                ReadOnly = true,
                WordWrap = true,
            };
            window.Add(log);
            var lines = new List<string>();
            bool busy = false;
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
                if (MessageBox.Query("Confirm setup", message, "Cancel", "Continue") != 1)
                    return;
                busy = true;
                foreach (var control in controls)
                    control.Enabled = false;
                cancellation = new CancellationTokenSource();
                Append($"Starting {action}…");
                try
                {
                    await Task.Run(async () =>
                    {
                        void Report(string text) => Application.MainLoop.Invoke(() => Append(text));
                        await new Installer(options, Report).Run(action, cancellation.Token);
                    });
                    Application.MainLoop.Invoke(() =>
                        Append("Done. You can select and copy the launch options above.")
                    );
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
                        cancellation.Dispose();
                        cancellation = null;
                        foreach (var control in controls)
                            control.Enabled = true;
                    });
                }
            }
            int column = 1;
            foreach (string action in new[] { "install", "update", "migrate", "uninstall" })
            {
                var button = new Button(char.ToUpperInvariant(action[0]) + action[1..])
                {
                    X = column,
                    Y = 13,
                };
                button.Clicked += () => Start(action);
                window.Add(button);
                controls.Add(button);
                column += action.Length + 5;
            }
            var cancel = new Button("Cancel task") { X = column, Y = 13 };
            cancel.Clicked += () => cancellation?.Cancel();
            window.Add(cancel);
            var quit = new Button("Back") { X = Pos.Right(cancel) + 1, Y = 13 };
            quit.Clicked += () =>
            {
                if (!busy)
                    Application.RequestStop();
            };
            window.Add(quit);
            top.Closing += args =>
            {
                if (busy)
                    args.Cancel = true;
            };
            top.Add(
                new StatusBar([
                    new StatusItem(Key.F2, "~F2~ Theme", TerminalTheme.Choose),
                    new StatusItem(
                        Key.CtrlMask | Key.Q,
                        "~Ctrl+Q~ Back",
                        () =>
                        {
                            if (!busy)
                                Application.RequestStop();
                        }
                    ),
                ])
            );
            Append(
                "Install or update from SpaceGT/Pulsar releases. Steam launch options will be shown here."
            );
            Application.Run(top);
        }
    }
}
