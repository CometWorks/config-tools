#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CometWorks.ConfigTools;
using Terminal.Gui;
using Xunit;

namespace PulsarConfigTests;

[Collection("ui-single-threaded")]
public class StartupUpdateTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Startup_only_prompts_for_updates_and_requires_an_explicit_choice(
        bool newer,
        bool accept,
        bool offline
    )
    {
        var driver = new FakeDriver();
        var loopType = typeof(FakeDriver).Assembly.GetType("Terminal.Gui.FakeMainLoop")!;
        var loop = (IMainLoopDriver)
            Activator.CreateInstance(
                loopType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [driver],
                null
            )!;
        Application.Init(driver, loop);
        try
        {
            TerminalTheme.Apply();
            using var owner = new Toplevel();
            bool prompted = false,
                requested = false;
            var release = new ToolRelease(
                "pulsarconfig-v9.0.0",
                new Version(9, 0, 0),
                "fixture",
                "fixture"
            );
            using var check = new StartupUpdateCheck(
                owner,
                value =>
                {
                    Assert.Same(release, value);
                    requested = true;
                },
                _ =>
                    offline
                        ? Task.FromException<ToolRelease?>(new IOException("Offline fixture"))
                        : Task.FromResult<ToolRelease?>(newer ? release : null)
            );
            var deadline = DateTime.UtcNow.AddSeconds(2);
            owner.Ready += () =>
                Application.MainLoop.AddTimeout(
                    TimeSpan.FromMilliseconds(20),
                    _ =>
                    {
                        if (
                            Application.Current != owner
                            && ((Dialog)Application.Current).Title.ToString()
                                == "Tool update available"
                        )
                        {
                            prompted = true;
                            if (accept)
                                Application.Current.ProcessKey(
                                    new KeyEvent(Key.Tab, new KeyModifiers())
                                );
                            Application.Current.ProcessKey(
                                new KeyEvent(Key.Enter, new KeyModifiers())
                            );
                        }
                        if (
                            Application.Current == owner
                            && (prompted || DateTime.UtcNow >= deadline)
                        )
                        {
                            Application.RequestStop(owner);
                            return false;
                        }
                        return true;
                    }
                );
            Application.Run(owner);
            Assert.Equal(newer && !offline, prompted);
            Assert.Equal(newer && accept && !offline, requested);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}
