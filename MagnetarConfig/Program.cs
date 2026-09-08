using CometWorks.ConfigTools;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Terminal.Gui;
using Magnetar.Config.Io;
using Magnetar.Config.Model;
using Magnetar.Config.Ui;

namespace Magnetar.Config;

internal static class Program
{
    private static int Main(string[] args)
    {
        // The deployed layout splits the tool like the launchers: the apphost
        // and MagnetarConfig.dll sit at the install root, the dependencies in
        // Libraries/MagnetarConfig (see the Deploy target in the csproj).
        // Install the resolver before Run is JITed — its body is the first to
        // touch Terminal.Gui types. A flat layout (dotnet run, tests) never
        // reaches the hook because everything resolves from the app folder.
        // AppContext also points at the executable directory in single-file builds.
        string libraryDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "MagnetarConfig");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string path = Path.Combine(
                libraryDir, new AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        if (SelfUpdate.Handle(args).GetAwaiter().GetResult() is int updateResult) return updateResult;
        if (args.Length > 0 && args[0] is "install" or "update" or "uninstall" or "check" or "--setup")
            return RunSetup(args);
        return Run(args);
    }

    private static int RunSetup(string[] args)
    {
        try
        {
            if (System.Linq.Enumerable.Contains(args, "--help")) { Cli.PrintHelp(); return 0; }
            bool tui = args[0] == "--setup";
            var values = (string[])args.Clone();
            if (tui) values[0] = "install";
            var options = Install.InstallOptions.Parse(values);
            if (tui)
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                    throw new InvalidOperationException("Setup needs a terminal. Use install/update/uninstall --yes for scripts.");
                using var palette = PaletteConsole.Attach();
                Application.UseSystemConsole = true;
                try
                {
                    Application.Init();
                    TerminalTheme.Apply(ThemePreference.Load(ThemePreference.FilePath, ThemeKind.Sandstone));
                    using var pointer = new PointerHighlight();
                    if (!options.TargetSpecified)
                    {
                        string selected = InstallationPicker.Show(Install.InstallationDiscovery.Create(), options.Target);
                        if (selected is null) return 0;
                        options.Target = selected;
                    }
                    Install.SetupUi.Run(options, checkUpdates: true);
                }
                finally { Application.Shutdown(); }
            }
            else
            {
                if (options.Action != "check" && !options.Yes)
                    throw new InvalidOperationException("Use --yes to confirm an installation action, or --setup for the TUI.");
                using var cancellation = new System.Threading.CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                new Install.Installer(options, Console.WriteLine).Run(options.Action, cancellation.Token).GetAwaiter().GetResult();
            }
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Magnetar setup cancelled."); return 130; }
        catch (Exception error) { Console.Error.WriteLine("Magnetar setup: " + error.Message); return 1; }
    }

    private static int Run(string[] args)
    {
        Cli cli = Cli.Parse(args);

        if (cli.Help)
        {
            Cli.PrintHelp();
            return 0;
        }
        if (cli.Error != null)
        {
            Console.Error.WriteLine(cli.Error);
            return 1;
        }

        // Headless read-only report — no Terminal.Gui, safe for scripts/CI.
        if (cli.Diag)
        {
            InstanceBinding diagBinding = cli.ToBinding();
            if (!System.IO.Directory.Exists(diagBinding.DataDir))
            {
                Console.Error.WriteLine($"Data dir does not exist: {diagBinding.DataDir}");
                return 1;
            }
            return Diagnostics.Run(diagBinding);
        }

        // Ensure the box-drawing / shade glyphs render on legacy Windows consoles.
        if (PlatformPaths.IsWindows)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        }

        // Use the same ANSI renderer on both platforms for RGB themes and the
        // terminal default background. The legacy -netdriver option remains accepted.
        using var palette = PaletteConsole.Attach();
        Application.UseSystemConsole = true;

        try
        {
            Application.Init();
            TerminalTheme.Apply(ThemePreference.Load(ThemePreference.FilePath, ThemeKind.Sandstone));

            using var pointer = new PointerHighlight();
            InstanceBinding binding = cli.ToBinding();
            if (cli.MagnetarExe == null && (!cli.HasInstance || !Install.Installer.IsPortable(InstanceLocator.InstallRoot)))
            {
                var catalog = Install.InstallationDiscovery.Create();
                string selected = catalog.SingleSavedInstallation()?.Path ?? InstallationPicker.Show(catalog);
                if (selected is null) return 0;
                if (!catalog.Inspect(selected).CanOpen)
                {
                    var setup = new Install.InstallOptions { Target = selected, Ds64 = binding.Ds64Dir };
                    Install.SetupUi.Run(setup);
                    selected = setup.Target;
                    if (!catalog.Inspect(selected).CanOpen) return 0;
                }
                binding.MagnetarExePath = InstanceLocator.DefaultMagnetarExe(selected);
                if (cli.ConfigDir == null) binding.MagnetarConfigDir = InstanceLocator.DefaultMagnetarConfigDir(selected);
            }

            // Windows ships two launchers (Legacy = .NET Framework 4.8, Interim =
            // .NET 10). Let the operator pick which to configure when both are
            // installed; auto-select when only one is. Skipped when the launcher
            // or config dir was pinned on the command line.
            if (PlatformPaths.IsWindows && cli.MagnetarExe == null && cli.ConfigDir == null)
            {
                System.Collections.Generic.IReadOnlyList<MagnetarLauncher> launchers =
                    InstanceLocator.PresentWindowsLaunchers(Path.GetDirectoryName(binding.MagnetarExePath));
                if (launchers.Count > 0)
                {
                    MagnetarLauncher chosen = launchers.Count == 1
                        ? launchers[0]
                        : ChooseLauncher(launchers);
                    if (chosen == null)
                        return 0; // cancelled at the launcher picker
                    binding.MagnetarConfigDir = chosen.ConfigDir;
                    binding.MagnetarExePath = chosen.ExePath;
                }
            }

            // No usable data dir given and the default does not exist → picker.
            if (!cli.HasInstance && !System.IO.Directory.Exists(binding.DataDir))
            {
                InstanceBinding chosen = InstancePickerDialog.Show(binding);
                if (chosen == null)
                    return 0;
                binding = chosen;
            }

            var shell = new AppShell(binding);
            using var startupUpdate = new StartupUpdateCheck(shell, shell.ToolUpdates);
            Application.Run(shell);
        }
        catch (Exception e)
        {
            Application.Shutdown();
            Console.Error.WriteLine("Fatal: " + e);
            return 1;
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }

    /// <summary>
    /// Startup prompt to pick which installed Windows launcher to configure.
    /// Returns the chosen launcher, or null if the operator cancelled.
    /// </summary>
    private static MagnetarLauncher ChooseLauncher(
        System.Collections.Generic.IReadOnlyList<MagnetarLauncher> launchers)
    {
        var buttons = new string[launchers.Count + 1];
        for (int i = 0; i < launchers.Count; i++)
            buttons[i] = launchers[i].Label;
        buttons[launchers.Count] = "Cancel";

        int pick = Dialogs.QueryDetails(
            "Select Magnetar",
            "Which Magnetar do you want to configure?",
            "Both launchers are installed on this machine.",
            error: false,
            buttons);

        return pick < launchers.Count ? launchers[pick] : null;
    }
}
