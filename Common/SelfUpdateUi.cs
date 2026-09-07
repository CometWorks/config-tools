#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

internal static class SelfUpdateUi
{
    /// <returns>True when the caller must close its application so the helper can update it.</returns>
    public static bool Show(ToolRelease? available = null)
    {
        bool prepared = false,
            busy = false;
        ToolRelease? release = available;
        using var cancel = new CancellationTokenSource();
        using var dialog = new Dialog("Update " + SelfUpdate.Tool)
        {
            Width = Dim.Percent(85),
            Height = 14,
            ColorScheme = TerminalTheme.Window,
        };
        var message = new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            Text = available is null ? "Checking releases…" : Describe(available),
        };
        var check = new Button("Check again");
        var update = new Button("Update and close") { Enabled = available != null };
        var close = new Button("Later") { IsDefault = true };
        bool closing = false;
        dialog.Add(message);
        dialog.AddButton(close);
        dialog.AddButton(update);
        dialog.AddButton(check);
        close.Clicked += () =>
        {
            closing = true;
            cancel.Cancel();
            if (!busy)
                Application.RequestStop();
        };
        dialog.Closing += args =>
        {
            if (busy)
            {
                closing = true;
                cancel.Cancel();
                args.Cancel = true;
            }
        };
        async void Run(bool install)
        {
            if (busy)
                return;
            busy = true;
            check.Enabled = update.Enabled = false;
            message.Text = install ? "Downloading and verifying update…" : "Checking releases…";
            try
            {
                if (install)
                {
                    await SelfUpdate.Prepare(release!, cancel.Token);
                    prepared = true;
                }
                else
                    release = await SelfUpdate.Check(cancel.Token);
                Application.MainLoop.Invoke(() =>
                {
                    busy = false;
                    if (prepared || closing)
                        Application.RequestStop();
                    else
                    {
                        message.Text =
                            release == null
                                ? $"{SelfUpdate.Tool} {SelfUpdate.VersionText} is up to date."
                                : Describe(release);
                        check.Enabled = true;
                        update.Enabled = release != null;
                    }
                });
            }
            catch (Exception error)
            {
                Application.MainLoop.Invoke(() =>
                {
                    busy = false;
                    if (closing)
                    {
                        Application.RequestStop();
                        return;
                    }
                    message.Text =
                        error is OperationCanceledException
                            ? "Update cancelled. Close this dialog to try again."
                            : error.Message;
                    check.Enabled = !cancel.IsCancellationRequested;
                    update.Enabled = !cancel.IsCancellationRequested && release != null;
                });
            }
        }
        check.Clicked += () => Run(false);
        update.Clicked += () => Run(true);
        if (available is null)
            dialog.Ready += () => Run(false);
        Application.Run(dialog);
        return prepared;
    }

    private static string Describe(ToolRelease release) =>
        $"Installed: {SelfUpdate.VersionText}\nAvailable: {release.Tag}\n\nUpdate verifies the download, keeps the previous executable as a backup, and closes this tool to replace it. Reopen it with your usual command afterwards. Close other windows of this tool first.";
}
