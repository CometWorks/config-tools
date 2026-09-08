using System.Diagnostics;
using System.Xml.Linq;
using CometWorks.ConfigTools;
using Magnetar.Config.Ui;
using Terminal.Gui;

namespace Pulsar.Config;

internal static class ConfigUi
{
    public static void Run(Options options)
    {
        // Xterm window operation, understood by Konsole and other supporting terminals.
        // Request once; the terminal can decline it and users can still resize afterwards.
        if (Console.WindowWidth < 128 || Console.WindowHeight < 40)
        {
            int width = Math.Max(128, Console.WindowWidth), height = Math.Max(40, Console.WindowHeight);
            Console.Write($"\x1b[8;{height};{width}t");
            Console.Out.Flush();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    width = Math.Min(width, Console.LargestWindowWidth);
                    height = Math.Min(height, Console.LargestWindowHeight);
                    Console.SetBufferSize(Math.Max(Console.BufferWidth, width), Math.Max(Console.BufferHeight, height));
                    Console.SetWindowSize(width, height);
                }
                catch (Exception error) when (error is IOException or ArgumentOutOfRangeException or PlatformNotSupportedException) { }
            }
        }
        using var palette = PaletteConsole.Attach();
        Application.UseSystemConsole = true;
        Application.Init();
        try
        {
            TerminalTheme.Apply(
                ThemePreference.Load(ThemePreference.FilePath, ThemeKind.Sandstone)
            );
            using var pointer = new PointerHighlight();
            if (!options.TargetSpecified && options.Config is null)
            {
                var catalog = InstallationDiscovery.Create();
                string? selected = catalog.SingleSavedInstallation()?.Path
                    ?? InstallationPicker.Show(catalog, options.Target);
                if (selected is null) return;
                options.Target = selected;
                if (!catalog.Inspect(selected).CanOpen)
                {
                    SetupUi.Run(options);
                    if (!catalog.Inspect(options.Target).CanOpen) return;
                }
            }
            using var shell = new ConfigShell(options);
            using var shortcuts = new GlobalShortcuts(shell);
            using var startupUpdate = new StartupUpdateCheck(
                shell,
                release =>
                {
                    if (SelfUpdateUi.Show(release))
                        Application.RequestStop();
                }
            );
            Application.Run(shell);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}

internal sealed class ConfigShell : Toplevel
{
    private readonly Options options;
    private PluginEditor editor;
    private View? panel;
    private readonly Label location;
    private readonly Label status;

    public ConfigShell(Options options)
    {
        this.options = options;
        editor = new PluginEditor(options);
        ColorScheme = TerminalTheme.Desktop;
        location = new Label("")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
        };
        status = new Label("Changes are saved with .bak backups and take effect next launch.")
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(1),
        };
        Add(
            location,
            status,
            new WorkspaceStatusBar(
                new[]
                {
                    new StatusItem(Key.F1, "~F1~ Home", Dashboard),
                    new StatusItem(Key.F2, "~F2~ Theme", TerminalTheme.Choose),
                    new StatusItem(Key.F3, "~F3~ Plugins", () => Safe(Plugins)),
                    new StatusItem(Key.F4, "~F4~ Profiles", () => Safe(Profiles)),
                    new StatusItem(Key.F5, "~F5~ Start game", StartGame),
                    new StatusItem(Key.F6, "~F6~ Dev folders", () => Safe(DevFolders)),
                    new StatusItem(Key.F7, "~F7~ Sources", () => Safe(Sources)),
                    new StatusItem(Key.F10, "~F10~ Quit", () => Application.RequestStop()),
                }
            )
        );
        Dashboard();
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Dialogs.Error("Pulsar configuration", error.Message);
        }
    }

    private void Show(View view, bool home = false)
    {
        if (panel is not null)
        {
            Remove(panel);
            panel.Dispose();
        }
        panel = view;
        view.X = Pos.Center();
        view.Y = 2;
        view.Width = Dim.Function(() => Math.Min(132, Application.Driver.Cols - 4));
        view.Height = Dim.Function(() => Math.Min(34, Application.Driver.Rows - 6));
        if (home)
        {
            view.X = Pos.Center();
            view.Y = 2;
            view.Width = Dim.Function(() => Math.Min(112, Application.Driver.Cols - 4));
            view.Height = Dim.Function(() => Math.Min(31, Application.Driver.Rows - 6));
        }
        Add(view);
        location.Text =
            $"{(editor.Game == "se2" ? "SE2" : "SE1")} · {editor.Target} · Config: {editor.ConfigDir}";
        view.SetFocus();
    }

    internal void Dashboard()
    {
        var window = new WorkspaceWindow("Pulsar") { ColorScheme = TerminalTheme.Window };
        window.Border.Effect3D = true;
        window.Add(new Label($"Space Engineers {(editor.Game == "se2" ? "2" : "1")}")
        { X = 3, Y = 1, ColorScheme = TerminalTheme.Title });
        window.Add(new Label("Game & plugins")
        { X = 3, Y = 3, ColorScheme = TerminalTheme.Desktop });
        window.Add(new Label("Installation & tool")
        { X = Pos.Percent(51), Y = 3, ColorScheme = TerminalTheme.Desktop });

        var gameActions = new (string Label, Action Run)[]
        {
            ("_Start game", StartGame),
            ("_Plugins", () => Safe(Plugins)),
            ("P_rofiles", () => Safe(Profiles)),
            ("_Dev folders", () => Safe(DevFolders)),
            ("S_ources", () => Safe(Sources)),
        };
        var toolActions = new (string Label, Action Run)[]
        {
            ("_Manage Pulsar…", Setup),
            ("Choose _installation…", () => Safe(OpenInstallation)),
            ("Check for tool _updates…", () =>
            {
                if (SelfUpdateUi.Show()) Application.RequestStop();
            }),
            ("_Theme…", TerminalTheme.Choose),
            ("_Quit", () => Application.RequestStop()),
        };
        for (int column = 0; column < 2; column++)
        {
            var actions = column == 0 ? gameActions : toolActions;
            for (int row = 0; row < actions.Length; row++)
            {
                var button = new WorkspaceButton(actions[row].Label)
                {
                    X = column == 0 ? 3 : Pos.Percent(51),
                    Y = 4 + row * 3,
                    Width = Dim.Percent(44),
                    Height = 3,
                };
                button.Clicked += actions[row].Run;
                window.Add(button);
            }
        }
        window.Add(new LineView
        { X = 3, Y = 20, Width = Dim.Fill(3), ColorScheme = TerminalTheme.Border });
        window.Add(new Label($"Installation:   {editor.Target}")
        { X = 3, Y = 21, Width = Dim.Fill(3), ColorScheme = TerminalTheme.Desktop });
        window.Add(new Label($"Configuration:  {editor.ConfigDir}")
        { X = 3, Y = Pos.Function(() => Application.Driver.Rows >= 38 ? 23 : 22),
            Width = Dim.Fill(3), ColorScheme = TerminalTheme.Desktop });
        var launch = new Label
        {
            X = 3,
            Y = Pos.Function(() => Application.Driver.Rows >= 38 ? 25 : 23),
            Width = Dim.Fill(3),
            Height = Dim.Function(() => Application.Driver.Rows >= 38 ? 3 : 1),
        };
        window.Add(launch);
        window.LayoutStarted += _ =>
        {
            string text = Application.Driver.Rows >= 38
                ? $"Steam launch options:\n{editor.LaunchOptions}"
                : $"Steam launch options: {editor.LaunchOptions}";
            if (launch.Text.ToString() != text) launch.Text = text;
        };
        Show(window, home: true);
    }

    private void OpenInstallation()
    {
        string? selected = InstallationPicker.Show(InstallationDiscovery.Create(), options.Target, allowNew: false);
        if (selected is null) return;
        var values = Form(
            "Open Pulsar installation",
            new[]
            {
                ("Installation", selected),
                ("Game (auto/se1/se2)", options.Game),
                ("Config override", options.Config ?? ""),
            }
        );
        if (values is null)
            return;
        string game = values["Game (auto/se1/se2)"].Trim().ToLowerInvariant();
        if (game is not ("auto" or "se1" or "se2"))
            throw new SetupError("Select auto, se1 or se2.");
        var candidate = new Options
        {
            Target = values["Installation"],
            Game = game,
            Config = string.IsNullOrWhiteSpace(values["Config override"])
                ? null
                : values["Config override"],
        };
        var next = new PluginEditor(candidate);
        options.Target = next.Target;
        options.Game = candidate.Game;
        options.Config = candidate.Config;
        editor = new PluginEditor(options);
        Dashboard();
    }

    private void Setup() =>
        Safe(() =>
        {
            status.Visible = false;
            try
            {
                SetupUi.Run(options);
                editor = new PluginEditor(options);
                Dashboard();
            }
            finally
            {
                status.Visible = true;
                SetNeedsDisplay();
            }
        });

    private async void StartGame()
    {
        try
        {
            var command = editor.LaunchCommand();
            command.RedirectStandardOutput = true;
            command.RedirectStandardError = true;
            using var process =
                Process.Start(command) ?? throw new SetupError("Steam could not be started.");
            status.Text = "Launch requested through Steam.";
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdout;
            string error = await stderr;
            int exitCode = process.ExitCode;
            if (exitCode != 0)
                Application.MainLoop?.Invoke(() =>
                    status.Text = $"Steam exited with code {exitCode}: {error.Trim()}"
                );
        }
        catch (Exception error)
        {
            Application.MainLoop?.Invoke(() => Dialogs.Error("Start game", error.Message));
        }
    }

    internal void Plugins()
    {
        var window = new WorkspaceWindow("Plugins · Space/Enter toggles the active profile")
        {
            ColorScheme = TerminalTheme.Window,
        };
        var filter = new TextField("")
        {
            X = 12,
            Y = 1,
            Width = Dim.Fill(3),
        };
        var list = new WorkspaceList
        {
            X = 3,
            Y = 4,
            Width = Dim.Percent(45),
            Height = Dim.Fill(5),
        };
        var details = new TextView
        {
            X = Pos.Percent(49),
            Y = 4,
            Width = Dim.Fill(3),
            Height = Dim.Fill(5),
            ColorScheme = TerminalTheme.HomeAction,
            ReadOnly = true,
            WordWrap = true,
        };
        List<PluginEntry> rows = [];
        void Detail()
        {
            var selected =
                list.SelectedItem >= 0 && list.SelectedItem < rows.Count
                    ? rows[list.SelectedItem]
                    : null;
            details.Text = selected is null
                ? "No matching plugins. Pulsar downloads remote catalogs on launch. Add sources or dev folders to make more plugins available."
                : $"{selected.Name}\n{selected.Kind} · {selected.Id}\n\n{selected.Details}\n\nDependencies: {string.Join(", ", selected.Dependencies)}";
        }
        void Refresh()
        {
            string query = filter.Text.ToString() ?? "";
            rows = editor
                .Plugins()
                .Where(p =>
                    $"{p.Name} {p.Id} {p.Kind}".Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            int keep = list.SelectedItem;
            list.SetSource(
                rows.Select(p => $"  {(p.Enabled ? "[x]" : "[ ]")} {p.Name} · {p.Kind}").ToList()
            );
            if (rows.Count > 0)
                list.SelectedItem = Math.Clamp(keep, 0, rows.Count - 1);
            Detail();
        }
        void Toggle() =>
            Safe(() =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= rows.Count)
                    return;
                editor.TogglePlugin(rows[list.SelectedItem]);
                Refresh();
            });
        filter.TextChanged += _ => Safe(Refresh);
        list.SelectedItemChanged += _ => Detail();
        list.OpenSelectedItem += _ => Toggle();
        list.KeyPress += args =>
        {
            if (args.KeyEvent.Key == Key.Space)
            {
                Toggle();
                args.Handled = true;
            }
        };
        window.Add(
            new Label("Filter")
            {
                X = 3,
                Y = 1,
                ColorScheme = TerminalTheme.Desktop,
            },
            filter,
            list,
            details
        );
        window.Add(
            new Label("Available plugins")
            {
                X = 3,
                Y = 3,
                ColorScheme = TerminalTheme.Title,
            },
            new Label("Details")
            {
                X = Pos.Percent(49),
                Y = 3,
                ColorScheme = TerminalTheme.Title,
            }
        );
        ActionDivider(window);
        Button(window, "_Toggle", 0, Toggle);
        Button(window, "_Refresh", 14, () => Safe(Refresh));
        Refresh();
        Show(window);
    }

    internal void Sources() => SourceScreen(false);

    internal void DevFolders() => SourceScreen(true);

    private void SourceScreen(bool devOnly)
    {
        var window = new WorkspaceWindow(
            devOnly ? "Dev folders · registration and active profile" : "Plugin sources"
        )
        {
            ColorScheme = TerminalTheme.Window,
        };
        var list = new WorkspaceList
        {
            X = 3,
            Y = 3,
            Width = Dim.Fill(3),
            Height = Dim.Fill(5),
        };
        var hint = new Label(
            devOnly
                ? "Registered sources and active-profile selection are independent."
                : "Enter edits a source. Changes apply on the next game launch."
        )
        {
            X = 3,
            Y = 1,
            Width = Dim.Fill(3),
            ColorScheme = TerminalTheme.Desktop,
        };
        var empty = new Label(
            devOnly
                ? "No dev folders registered. Choose Add to select a plugin manifest."
                : "No sources registered. Choose Add to connect a plugin catalog or repository."
        )
        {
            X = 3,
            Y = 3,
            Width = Dim.Fill(3),
            Height = 2,
            ColorScheme = TerminalTheme.Desktop,
        };
        List<SourceEntry> rows = [];
        void Refresh()
        {
            rows = editor
                .SourcesList()
                .Where(s => devOnly ? s.Kind == "LocalPlugin" : s.Kind != "LocalPlugin")
                .ToList();
            empty.Visible = rows.Count == 0;
            int keep = list.SelectedItem;
            list.SetSource(
                rows.Select(s =>
                        devOnly
                            ? $"  {(s.Enabled ? "[x]" : "[ ]")} {(editor.DevState(s.Id).Active ? "ACTIVE" : "off   ")} {s.Name} · {s.Key} [{s.Data.Element("File")?.Value}]"
                            : $"  {(s.Enabled ? "[x]" : "[ ]")} {s.Kind, -13} {s.Name} · {s.Key}"
                    )
                    .ToList()
            );
            if (rows.Count > 0)
                list.SelectedItem = Math.Clamp(keep, 0, rows.Count - 1);
        }
        SourceEntry? Selected() =>
            list.SelectedItem >= 0 && list.SelectedItem < rows.Count
                ? rows[list.SelectedItem]
                : null;
        void Edit() =>
            Safe(() =>
            {
                if (Selected() is { } row)
                    EditSource(row.Kind, row);
                Refresh();
            });
        list.OpenSelectedItem += _ => Edit();
        window.Add(list, hint, empty);
        ActionDivider(window);
        Button(
            window,
            "_Add…",
            0,
            () =>
                Safe(() =>
                {
                    if (devOnly)
                        EditSource("LocalPlugin", null);
                    else
                    {
                        int kind = MessageBox.Query(
                            "Add source",
                            "Choose the source type.",
                            "Cancel",
                            "Hub",
                            "Plugin repo",
                            "Local hub",
                            "Workshop"
                        );
                        if (kind is >= 1 and <= 4)
                            EditSource(
                                new[] { "", "RemoteHub", "RemotePlugin", "LocalHub", "Mod" }[kind],
                                null
                            );
                    }
                    Refresh();
                })
        );
        Button(window, "_Edit…", 12, Edit);
        Button(
            window,
            "_Remove",
            25,
            () =>
                Safe(() =>
                {
                    if (Selected() is not { } row)
                        return;
                    if (
                        Dialogs.Confirm(
                            "Remove source",
                            $"Unregister {row.Name}?\nSource files and saved profile selections are kept."
                        )
                    )
                    {
                        editor.RemoveSource(row);
                        Refresh();
                    }
                })
        );
        Button(window, "Re_fresh", 41, () => Safe(Refresh));
        Refresh();
        Show(window);
    }

    private void EditSource(string kind, SourceEntry? original)
    {
        XElement value = original is null
            ? new XElement(
                kind,
                new XElement("Enabled", true),
                kind is "RemoteHub" or "RemotePlugin" ? new XElement("Trusted", true) : null
            )
            : new XElement(original.Data);
        if (original is null && kind == "LocalPlugin")
        {
            string? path = FileDialogs.PickFile(
                "Add dev folder",
                "Select the plugin manifest XML",
                Files.Home,
                new[] { ".xml" }
            );
            if (path is null)
                return;
            value.SetElementValue("Folder", Path.GetDirectoryName(path));
            value.SetElementValue("File", Path.GetFileName(path));
            value.SetElementValue("Name", Path.GetFileName(Path.GetDirectoryName(path)));
        }
        var fields = new List<(string, string)> { ("Name", value.Element("Name")?.Value ?? "") };
        if (kind is "RemoteHub" or "RemotePlugin")
        {
            fields.Add(("Repo", value.Element("Repo")?.Value ?? ""));
            fields.Add(("Branch", value.Element("Branch")?.Value ?? "main"));
        }
        if (kind is "LocalHub" or "LocalPlugin")
            fields.Add(("Folder", value.Element("Folder")?.Value ?? ""));
        if (kind is "RemotePlugin" or "LocalPlugin")
            fields.Add(("File", value.Element("File")?.Value ?? ""));
        if (kind == "Mod")
            fields.Add(("ID", value.Element("ID")?.Value ?? ""));
        fields.Add(("Enabled", value.Element("Enabled")?.Value ?? "true"));
        if (kind is "RemoteHub" or "RemotePlugin")
            fields.Add(("Trusted", value.Element("Trusted")?.Value ?? "false"));
        if (kind == "LocalPlugin")
        {
            var state = original is null
                ? (Active: true, Debug: true)
                : editor.DevState(original.Id);
            fields.Add(("Active profile", state.Active.ToString().ToLowerInvariant()));
            fields.Add(("Debug build", state.Debug.ToString().ToLowerInvariant()));
        }
        var edited = Form(original is null ? "Add " + kind : "Edit " + original.Name, fields);
        if (edited is null)
            return;
        foreach (var (name, text) in edited)
            if (name is not ("Active profile" or "Debug build"))
                value.SetElementValue(name, text.Trim());
        editor.PutSource(
            original,
            value,
            kind == "LocalPlugin" ? bool.Parse(edited["Active profile"]) : null,
            kind != "LocalPlugin" || bool.Parse(edited["Debug build"])
        );
    }

    internal void Profiles()
    {
        var window = new WorkspaceWindow("Plugin profiles · Current.xml is the active set")
        {
            ColorScheme = TerminalTheme.Window,
        };
        var list = new WorkspaceList
        {
            X = 3,
            Y = 3,
            Width = Dim.Fill(3),
            Height = Dim.Fill(5),
        };
        var empty = new Label(
            "No saved profiles. Choose Save as to keep your current plugin selection."
        )
        {
            X = 3,
            Y = 3,
            Width = Dim.Fill(3),
            Height = 2,
            ColorScheme = TerminalTheme.Desktop,
        };
        List<(string Key, string Name)> rows = [];
        void Refresh()
        {
            rows = editor.Profiles().ToList();
            empty.Visible = rows.Count == 0;
            list.SetSource(rows.Select(p => "  " + p.Name).ToList());
        }
        string? Selected() =>
            list.SelectedItem >= 0 && list.SelectedItem < rows.Count
                ? rows[list.SelectedItem].Key
                : null;
        void Load() =>
            Safe(() =>
            {
                if (Selected() is not { } key)
                    return;
                if (!Dialogs.Confirm("Load profile", $"Replace the active plugin set with {key}?"))
                    return;
                editor.LoadProfile(key);
                status.Text = $"Loaded profile {key}. Takes effect on the next launch.";
            });
        list.OpenSelectedItem += _ => Load();
        window.Add(
            list,
            new Label("Enter loads a profile. Save as captures your current plugin selection.")
            {
                X = 3,
                Y = 1,
                Width = Dim.Fill(3),
                ColorScheme = TerminalTheme.Desktop,
            }
        );
        window.Add(empty);
        ActionDivider(window);
        Button(window, "_Load", 0, Load);
        Button(
            window,
            "_Save as…",
            12,
            () =>
                Safe(() =>
                {
                    string? name = Dialogs.Prompt("Save active profile", "New profile name:");
                    if (name is null)
                        return;
                    editor.SaveProfile(name);
                    Refresh();
                })
        );
        Button(
            window,
            "_Update",
            28,
            () =>
                Safe(() =>
                {
                    if (
                        Selected() is { } key
                        && Dialogs.Confirm(
                            "Update profile",
                            $"Overwrite {key} with the active plugin set?"
                        )
                    )
                    {
                        editor.UpdateProfile(key);
                        Refresh();
                    }
                })
        );
        Button(
            window,
            "Re_name…",
            42,
            () =>
                Safe(() =>
                {
                    if (Selected() is not { } key)
                        return;
                    string? name = Dialogs.Prompt("Rename profile", "New name:", key);
                    if (name is null)
                        return;
                    editor.SaveProfile(name, key);
                    Refresh();
                })
        );
        Button(
            window,
            "_Delete",
            57,
            () =>
                Safe(() =>
                {
                    if (
                        Selected() is { } key
                        && Dialogs.Confirm(
                            "Delete profile",
                            $"Delete {key}? The active set is kept."
                        )
                    )
                    {
                        editor.DeleteProfile(key);
                        Refresh();
                    }
                })
        );
        Refresh();
        Show(window);
    }

    private static void Button(View parent, string text, int x, Action action)
    {
        var button = new WorkspaceButton(text)
        {
            X = x + 3,
            Y = Pos.AnchorEnd(3),
            Height = 3,
        };
        button.Clicked += action;
        parent.Add(button);
    }

    private static void ActionDivider(View parent) =>
        parent.Add(
            new LineView
            {
                X = 3,
                Y = Pos.AnchorEnd(4),
                Width = Dim.Fill(3),
                ColorScheme = TerminalTheme.Border,
            }
        );

    private static Dictionary<string, string>? Form(
        string title,
        IEnumerable<(string Name, string Value)> fields
    )
    {
        var items = fields.ToList();
        var cancel = new WorkspaceButton("Cancel");
        var save = new WorkspaceButton("Save") { IsDefault = true };
        using var dialog = new WorkspaceDialog(
            title,
            Math.Min(96, Application.Driver.Cols - 4),
            items.Count * 2 + 7,
            cancel,
            save
        )
        {
            ColorScheme = TerminalTheme.Dialog,
        };
        var inputs = new Dictionary<string, View>();
        for (int i = 0; i < items.Count; i++)
        {
            var (name, value) = items[i];
            dialog.Add(
                new Label(name)
                {
                    X = 3,
                    Y = i * 2 + 2,
                    ColorScheme = TerminalTheme.Desktop,
                }
            );
            View input = name is "Enabled" or "Trusted" or "Active profile" or "Debug build"
                ? new CheckBox("") { Checked = value is "true" or "1" }
                : new TextField(value) { Width = Dim.Fill(3) };
            input.X = 24;
            input.Y = i * 2 + 2;
            inputs.Add(name, input);
            dialog.Add(input);
        }
        Dictionary<string, string>? result = null;
        cancel.Clicked += () => Application.RequestStop();
        save.Clicked += () =>
        {
            result = inputs.ToDictionary(
                p => p.Key,
                p =>
                    p.Value is CheckBox flag
                        ? flag.Checked.ToString().ToLowerInvariant()
                        : ((TextField)p.Value).Text.ToString() ?? ""
            );
            Application.RequestStop();
        };
        inputs.Values.First().SetFocus();
        Application.Run(dialog);
        return result;
    }
}
