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
        using var palette = PaletteConsole.Attach();
        Application.UseSystemConsole = true;
        Application.Init();
        try
        {
            TerminalTheme.Apply(ThemePreference.Load(ThemePreference.FilePath, ThemeKind.Sandstone));
            using var shell = new ConfigShell(options);
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
            Y = 1,
            Width = Dim.Fill(1),
        };
        status = new Label("Changes are saved with .bak backups and take effect next launch.")
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(1),
        };
        Add(
            new MenuBar(
                new[]
                {
                    new MenuBarItem(
                        "_File",
                        new[]
                        {
                            new MenuItem("_Open installation…", "", () => Safe(OpenInstallation)),
                            new MenuItem("_Setup / update / migrate…", "", Setup),
                            new MenuItem("_Quit", "", () => Application.RequestStop()),
                        }
                    ),
                    new MenuBarItem(
                        "_Game",
                        new[]
                        {
                            new MenuItem("_Overview", "", Dashboard),
                            new MenuItem("_Start through Steam", "", StartGame),
                        }
                    ),
                    new MenuBarItem(
                        "_Plugins",
                        new[]
                        {
                            new MenuItem("_Available / enabled", "", () => Safe(Plugins)),
                            new MenuItem("_Dev folders", "", () => Safe(DevFolders)),
                            new MenuItem("_Sources", "", () => Safe(Sources)),
                            new MenuItem("_Profiles", "", () => Safe(Profiles)),
                        }
                    ),
                    new MenuBarItem(
                        "_Tools",
                        new[]
                        {
                            new MenuItem("_Theme…", "", TerminalTheme.Choose),
                            new MenuItem(
                                "Tool _updates…",
                                "",
                                () =>
                                {
                                    if (SelfUpdateUi.Show())
                                        Application.RequestStop();
                                }
                            ),
                        }
                    ),
                }
            ),
            location,
            status,
            new StatusBar(
                new[]
                {
                    new StatusItem(Key.F2, "~F2~ Theme", TerminalTheme.Choose),
                    new StatusItem(Key.F3, "~F3~ Plugins", () => Safe(Plugins)),
                    new StatusItem(Key.F4, "~F4~ Profiles", () => Safe(Profiles)),
                    new StatusItem(Key.F5, "~F5~ Start game", StartGame),
                    new StatusItem(Key.F6, "~F6~ Dev folders", () => Safe(DevFolders)),
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

    private void Show(View view)
    {
        if (panel is not null)
        {
            Remove(panel);
            panel.Dispose();
        }
        panel = view;
        view.X = 1;
        view.Y = 2;
        view.Width = Dim.Fill(1);
        view.Height = Dim.Fill(2);
        Add(view);
        location.Text =
            $"{(editor.Game == "se2" ? "SE2" : "SE1")} · {editor.Target} · Config: {editor.ConfigDir}";
        view.SetFocus();
    }

    internal void Dashboard()
    {
        var window = new Window("Pulsar") { ColorScheme = TerminalTheme.Window };
        var text = new TextView
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4),
            ReadOnly = true,
            // Home information is passive; keyboard focus belongs to the action buttons.
            CanFocus = false,
            WordWrap = true,
            Text =
                $"Space Engineers {(editor.Game == "se2" ? "2" : "1")}\n\nInstallation: {editor.Target}\nConfiguration: {editor.ConfigDir}\n\n"
                + $"Steam launch options:\n{editor.LaunchOptions}\n\n"
                + "Start game uses Steam's existing launch options, including your extra arguments.\n"
                + "The selected installation/configuration should match that Steam setup.\n\n"
                + "Plugins: browse cached catalogs and enable plugins or local DLLs.\n"
                + "Dev folders: select a manifest, register its folder, and enable it in the active profile.\n"
                + "Sources: edit hubs, repositories, local catalogs, and Workshop sources.\n"
                + "Profiles: save, load, update, rename, and delete plugin presets.\n\n"
                + "For a -home launch or another custom config location, use File → Open installation.",
        };
        window.Add(text);
        Button(window, "_Start game", 1, StartGame);
        Button(window, "_Open installation…", 20, () => Safe(OpenInstallation));
        Button(window, "_Setup / update…", 46, Setup);
        Show(window);
    }

    private void OpenInstallation()
    {
        var values = Form(
            "Open Pulsar installation",
            new[]
            {
                ("Installation", options.Target),
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
            SetupUi.Run(options);
            editor = new PluginEditor(options);
            Dashboard();
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
        var window = new Window("Plugins · Space/Enter toggles the active profile")
        {
            ColorScheme = TerminalTheme.Window,
        };
        var filter = new TextField("")
        {
            X = 9,
            Y = 0,
            Width = Dim.Fill(1),
        };
        var list = new ListView
        {
            X = 0,
            Y = 2,
            Width = Dim.Percent(48),
            Height = Dim.Fill(2),
        };
        var details = new TextView
        {
            X = Pos.Percent(49),
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
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
                rows.Select(p => $"{(p.Enabled ? "[x]" : "[ ]")} {p.Name} · {p.Kind}").ToList()
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
        window.Add(new Label("Filter") { X = 1, Y = 0 }, filter, list, details);
        Button(window, "_Toggle", 0, Toggle);
        Button(window, "_Refresh", 14, () => Safe(Refresh));
        Refresh();
        Show(window);
    }

    internal void Sources() => SourceScreen(false);

    internal void DevFolders() => SourceScreen(true);

    private void SourceScreen(bool devOnly)
    {
        var window = new Window(
            devOnly ? "Dev folders · registration and active profile" : "Plugin sources"
        )
        {
            ColorScheme = TerminalTheme.Window,
        };
        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(4),
        };
        var hint = new Label(
            devOnly
                ? "Registered sources and active-profile selection are independent."
                : "Enter edits a source. Changes apply on the next game launch."
        )
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
        };
        List<SourceEntry> rows = [];
        void Refresh()
        {
            rows = editor
                .SourcesList()
                .Where(s => devOnly ? s.Kind == "LocalPlugin" : s.Kind != "LocalPlugin")
                .ToList();
            int keep = list.SelectedItem;
            list.SetSource(
                rows.Select(s =>
                        devOnly
                            ? $"{(s.Enabled ? "[x]" : "[ ]")} {(editor.DevState(s.Id).Active ? "ACTIVE" : "off   ")} {s.Name} · {s.Key} [{s.Data.Element("File")?.Value}]"
                            : $"{(s.Enabled ? "[x]" : "[ ]")} {s.Kind, -13} {s.Name} · {s.Key}"
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
        window.Add(list, hint);
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
        var window = new Window("Plugin profiles · Current.xml is the active set")
        {
            ColorScheme = TerminalTheme.Window,
        };
        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        List<(string Key, string Name)> rows = [];
        void Refresh()
        {
            rows = editor.Profiles().ToList();
            list.SetSource(rows.Select(p => p.Name).ToList());
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
        window.Add(list);
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
        var button = new Button(text) { X = x, Y = Pos.AnchorEnd(1) };
        button.Clicked += action;
        parent.Add(button);
    }

    private static Dictionary<string, string>? Form(
        string title,
        IEnumerable<(string Name, string Value)> fields
    )
    {
        var items = fields.ToList();
        var cancel = new Button("Cancel");
        var save = new Button("Save", true);
        using var dialog = new Dialog(title, 86, items.Count * 2 + 5, cancel, save)
        {
            ColorScheme = TerminalTheme.Dialog,
        };
        var inputs = new Dictionary<string, View>();
        for (int i = 0; i < items.Count; i++)
        {
            var (name, value) = items[i];
            dialog.Add(new Label(name) { X = 1, Y = i * 2 + 1 });
            View input = name is "Enabled" or "Trusted" or "Active profile" or "Debug build"
                ? new CheckBox("") { Checked = value is "true" or "1" }
                : new TextField(value) { Width = Dim.Fill(2) };
            input.X = 22;
            input.Y = i * 2 + 1;
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
