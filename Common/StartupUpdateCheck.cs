#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

/// <summary>A bounded background check; never blocks startup or interrupts an open dialog.</summary>
internal sealed class StartupUpdateCheck : IDisposable
{
    private readonly CancellationTokenSource cancel = new(TimeSpan.FromSeconds(8));
    private readonly object timer;
    private readonly MainLoop loop;
    private bool finished;

    public StartupUpdateCheck(
        Toplevel owner,
        Action<ToolRelease> update,
        Func<CancellationToken, Task<ToolRelease?>>? check = null,
        Func<bool>? canPrompt = null
    )
    {
        var token = cancel.Token;
        var task = Task.Run(async () =>
        {
            try
            {
                return await (check ?? SelfUpdate.Check)(token).ConfigureAwait(false);
            }
            catch
            {
                return null;
            } // Offline/rate-limited startup remains usable; manual check reports errors.
        });
        bool queued = false;
        bool CanPrompt() =>
            Application.Current == owner
            && owner.MenuBar?.IsMenuOpen != true
            && canPrompt?.Invoke() != false;
        loop = Application.MainLoop;
        timer = loop.AddTimeout(
            TimeSpan.FromMilliseconds(250),
            _ =>
            {
                if (finished)
                    return false;
                if (!task.IsCompleted)
                    return true;
                var release = task.GetAwaiter().GetResult();
                if (release is null)
                    return false;
                if (queued || !CanPrompt())
                    return true;
                queued = true;
                // Open modals after RunTimers returns so nested dialogs retain live timers.
                loop.Invoke(() =>
                {
                    queued = false;
                    if (finished || !CanPrompt())
                        return;
                    finished = true;
                    if (
                        MessageBox.Query(
                            "Tool update available",
                            $"{SelfUpdate.Tool} {release.Version} is available (installed: {SelfUpdate.VersionText}).\n\nUpdate this configuration tool now? Your game/server installation and settings are kept.",
                            "Later",
                            "Update…"
                        ) == 1
                    )
                        update(release);
                });
                return true;
            }
        );
    }

    public void Dispose()
    {
        finished = true;
        cancel.Cancel();
        loop.RemoveTimeout(timer);
        cancel.Dispose();
    }
}
