#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

internal static class SelfUpdateUi
{
    /// <returns>True when the caller must close its application so the helper can update it.</returns>
    public static bool Show()
    {
        bool prepared = false,
            busy = false;
        ToolRelease? release = null;
        using var cancel = new CancellationTokenSource();
        using var dialog = new Dialog("Update " + SelfUpdate.Tool)
        {
            Width = Dim.Percent(85),
            Height = 12,
            ColorScheme = TerminalTheme.Window,
        };
        var message = new TextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            ReadOnly = true,
            WordWrap = true,
            Text =
                $"Installed: {SelfUpdate.VersionText}\nCheck for a newer stable {SelfUpdate.Tool} release. Only this tool's executable is updated.",
        };
        var check = new Button("Check") { IsDefault = true };
        var update = new Button("Update and restart") { Enabled = false };
        var close = new Button("Close");
        dialog.Add(message);
        dialog.AddButton(check);
        dialog.AddButton(update);
        dialog.AddButton(close);
        close.Clicked += () =>
        {
            cancel.Cancel();
            if (!busy)
                Application.RequestStop();
        };
        dialog.Closing += args =>
        {
            if (busy)
            {
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
                    await SelfUpdate.Prepare(release!, restart: true, cancel.Token);
                    prepared = true;
                }
                else
                    release = await SelfUpdate.Check(cancel.Token);
                Application.MainLoop.Invoke(() =>
                {
                    busy = false;
                    if (prepared)
                        Application.RequestStop();
                    else
                    {
                        message.Text =
                            release == null
                                ? $"{SelfUpdate.Tool} {SelfUpdate.VersionText} is up to date."
                                : $"Installed: {SelfUpdate.VersionText}\nAvailable: {release.Tag}\n\nUpdate verifies SHA-256, keeps the previous executable, and restarts this tool with the same arguments. Close other windows of this tool first.";
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
        Application.Run(dialog);
        return prepared;
    }
}
