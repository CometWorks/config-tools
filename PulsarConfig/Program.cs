using System.Runtime.InteropServices;

namespace Pulsar.Config;

internal sealed class Options
{
    public string? Action,
        Source,
        Settings,
        Archive,
        Sha256;
    public string Target =
        Environment.GetEnvironmentVariable("PULSAR_DATA_DIR")
        ?? (
            Installer.Modern(AppContext.BaseDirectory)
                ? AppContext.BaseDirectory
                : Path.Combine(Files.DataHome, "Pulsar")
        );
    public string Version = "latest",
        Game = "auto";
    public bool Yes,
        Help;

    public static Options Parse(string[] args)
    {
        args = args.SelectMany(arg =>
                arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains('=')
                    ? arg.Split('=', 2)
                    : new[] { arg }
            )
            .ToArray();
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value() =>
                ++i < args.Length ? args[i] : throw new SetupError($"Missing value for {arg}.");
            switch (arg)
            {
                case "install" or "update" or "migrate" or "uninstall" when options.Action is null:
                    options.Action = arg;
                    break;
                case "--target":
                    options.Target = Value();
                    break;
                case "--source":
                    options.Source = Value();
                    break;
                case "--settings":
                    options.Settings = Value();
                    break;
                case "--archive":
                    options.Archive = Value();
                    break;
                case "--sha256":
                    options.Sha256 = Value();
                    break;
                case "--version":
                    options.Version = Value();
                    break;
                case "--game":
                    options.Game = Value();
                    break;
                case "--yes":
                    options.Yes = true;
                    break;
                case "--help" or "-h":
                    options.Help = true;
                    break;
                default:
                    throw new SetupError($"Unknown argument: {arg}");
            }
        }
        if (options.Game is not ("auto" or "se1" or "se2"))
            throw new SetupError("Choose --game auto, se1 or se2.");
        return options;
    }
}

internal static class Program
{
    [DllImport("libc")]
    private static extern uint geteuid();

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Help)
            {
                Console.WriteLine(
                    """
                    Pulsar Linux setup — omit the action to open the terminal UI.
                    PulsarConfig [install|update|migrate|uninstall] [options]

                    --target DIR     Installation folder
                    --game GAME      auto (saved choice), se1 or se2
                    --source DIR     Old native installation (migration; default: target)
                    --settings DIR   Old configuration (default: XDG_CONFIG_HOME/Pulsar)
                    --version TAG    Pulsar release tag, or latest
                    --archive FILE   Use a local unified Linux .tar.gz
                    --sha256 HEX     Expected checksum for a local archive
                    --yes            Confirm an explicitly named CLI action
                    --help           Show this help
                    """
                );
                return 0;
            }
            if (!OperatingSystem.IsLinux() || RuntimeInformation.OSArchitecture != Architecture.X64)
                throw new SetupError("Pulsar setup currently supports native Linux x64 installs.");
            if (geteuid() == 0)
                throw new SetupError("Run as your normal Steam user, without sudo.");
            if (options.Action is null)
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                    throw new SetupError(
                        "The TUI needs a terminal. Use an action with --yes for scripts."
                    );
                SetupUi.Run(options);
            }
            else
            {
                if (!options.Yes)
                    throw new SetupError(
                        "Use --yes to confirm a CLI action, or omit the action for the TUI."
                    );
                using var cancellation = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };
                await new Installer(options, Console.WriteLine).Run(
                    options.Action,
                    cancellation.Token
                );
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Setup cancelled.");
            return 130;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Pulsar setup: {error.Message}");
            return 1;
        }
    }
}

internal sealed class SetupError(string message) : Exception(message);
