using CometWorks.ConfigTools;
using System.Text.Json;

namespace Pulsar.Config;

internal static class InstallationDiscovery
{
    internal static string StateDirectory => Path.Combine(Files.StateHome, "pulsar-installer");

    public static InstallationCatalog Create(string? stateDirectory = null, string? historyPath = null) => new(
        "Pulsar", stateDirectory ?? StateDirectory, Probe,
        new[] { Environment.GetEnvironmentVariable("PULSAR_DATA_DIR") ?? Path.Combine(Files.DataHome, "Pulsar"),
            Path.Combine(Files.DataHome, "Pulsar"), AppContext.BaseDirectory, Environment.CurrentDirectory }, historyPath);

    private static Installation Probe(string path)
    {
        if (Installer.Legacy(path)) return new(path, InstallationKind.Older);
        bool se1 = Installer.Required.All(name => File.Exists(Path.Combine(path, name)));
        bool se2 = Installer.Se2Required.All(name => File.Exists(Path.Combine(path, name)));
        if (se1 || se2) return new(path, InstallationKind.Current, se1 && se2 ? "SE1 / SE2" : se1 ? "SE1" : "SE2");
        bool partial = File.Exists(Path.Combine(path, "Libraries/Interim/Pulsar.Shared.dll"))
            || File.Exists(Path.Combine(path, "Libraries/Modern/Pulsar.Shared.dll"))
            || (File.Exists(Path.Combine(path, "Interim.dll")) && File.Exists(Path.Combine(path, "Interim.runtimeconfig.json")));
        return new(path, partial ? InstallationKind.Incomplete : InstallationKind.Unknown);
    }

    internal static string ResolveGame(string target, string receipt)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(receipt));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("game", out var game)
                && game.ValueKind == JsonValueKind.String && game.GetString() is "se1" or "se2") return game.GetString()!;
        }
        catch (Exception error) when (InstallationCatalog.IsPathError(error) || error is JsonException) { }
        return !Installer.Required.All(name => File.Exists(Path.Combine(target, name)))
            && Installer.Se2Required.All(name => File.Exists(Path.Combine(target, name))) ? "se2" : "se1";
    }
}
