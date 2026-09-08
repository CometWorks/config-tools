using System.Reflection;
using CometWorks.ConfigTools;
using Pulsar.Config;
using Terminal.Gui;
using Xunit;

namespace PulsarConfigTests;

public class PointerHighlightTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_navigation_waits_for_modal_cleanup_and_keeps_bar_unfocusable(bool mouse)
    {
        var driver = new FakeDriver();
        var loop = (IMainLoopDriver)Activator.CreateInstance(
            typeof(FakeDriver).Assembly.GetType("Terminal.Gui.FakeMainLoop")!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [driver], null)!;
        Application.Init(driver, loop);
        try
        {
            using var pointer = new PointerHighlight();
            using var owner = new Toplevel();
            bool navigated = false, busy = true, cancellationRequested = false;
            var bar = new WorkspaceStatusBar([
                new StatusItem(Key.F1, "~F1~ Home", () =>
                {
                    Assert.Same(owner, Application.Current);
                    navigated = true;
                }),
            ]);
            owner.Add(bar);
            using var shortcuts = new GlobalShortcuts(owner);
            var outer = Application.Begin(owner);
            try
            {
                using var modal = new Dialog("Busy setup");
                modal.Closing += args =>
                {
                    cancellationRequested = true;
                    args.Cancel = busy;
                };
                var inner = Application.Begin(modal);
                modal.Running = true;
                try
                {
                    if (mouse)
                    {
                        var click = new MouseEvent
                        {
                            X = 5, Y = driver.Rows - 1, View = modal,
                            Flags = MouseFlags.Button1Clicked,
                        };
                        Application.RootMouseEvent(click);
                        Assert.True(click.Handled);
                        Assert.True(PointerHighlight.IsOver(bar));
                    }
                    else
                        Assert.True(Application.RootKeyEvent(new KeyEvent(Key.F1, new KeyModifiers())));
                    Assert.True(cancellationRequested);
                    Assert.True(modal.Running);
                    Assert.False(navigated);
                    Assert.False(bar.CanFocus);
                    busy = false;
                    Application.Iteration();
                    Assert.False(modal.Running);
                    Assert.False(navigated);
                }
                finally { Application.End(inner); }
                Application.Iteration();
                Application.MainLoop.MainIteration();
                Assert.True(navigated);
            }
            finally { Application.End(outer); }
        }
        finally { Application.Shutdown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_preserves_focus_and_selection_and_keyboard_takes_priority(bool turbo)
    {
        var driver = new FakeDriver();
        var loop = (IMainLoopDriver)
            Activator.CreateInstance(
                typeof(FakeDriver).Assembly.GetType("Terminal.Gui.FakeMainLoop")!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [driver],
                null
            )!;
        Application.Init(driver, loop);
        try
        {
            TerminalTheme.Apply(turbo ? ThemeKind.Turbo : ThemeKind.Sandstone);
            using var pointer = new PointerHighlight();
            using var top = new Toplevel();
            var first = new WorkspaceButton("First")
            {
                X = 1,
                Y = 1,
                Height = 3,
            };
            var second = new WorkspaceButton("Second")
            {
                X = 1,
                Y = 4,
                Height = 3,
            };
            var list = new WorkspaceList
            {
                X = 1,
                Y = 8,
                Width = 30,
                Height = 4,
            };
            list.SetSource(new[] { "  One", "  Two" });
            var status = new WorkspaceStatusBar([
                new StatusItem(Key.F1, "~F1~ Home", () => { }),
                new StatusItem(Key.F10, "~F10~ Quit", () => { }, () => false),
            ]);
            top.Add(first, second, list, status);
            int firstClicks = 0,
                secondClicks = 0;
            first.Clicked += () => firstClicks++;
            second.Clicked += () => secondClicks++;
            var state = Application.Begin(top);
            try
            {
                first.SetFocus();
                var mouse = new MouseEvent
                {
                    X = 2,
                    Y = 5,
                    View = second,
                    Flags = MouseFlags.ReportMousePosition,
                };
                Application.RootMouseEvent(mouse);
                first.Redraw(first.Bounds);
                second.Redraw(second.Bounds);
                Assert.True(first.HasFocus);
                Assert.True(PointerHighlight.IsOver(second));
                Assert.Equal(TerminalTheme.HomeAction.Normal, first.ColorScheme.Focus);
                Assert.Equal(TerminalTheme.HomeAction.Focus, second.ColorScheme.Normal);
                Assert.Equal(0, firstClicks + secondClicks);

                var key = new KeyEvent(Key.Enter, new KeyModifiers());
                Assert.False(Application.RootKeyEvent(key));
                top.ProcessKey(key);
                Assert.False(PointerHighlight.MouseActive);
                Assert.Equal(1, firstClicks);
                Assert.Equal(0, secondClicks);
                first.Redraw(first.Bounds);
                Assert.Equal(TerminalTheme.HomeAction.Focus, first.ColorScheme.Focus);
                Application.RootMouseEvent(mouse); // An unchanged pointer does not reclaim priority.
                Assert.False(PointerHighlight.MouseActive);

                Application.RootMouseEvent(
                    new MouseEvent
                    {
                        X = 2,
                        Y = 9,
                        View = list,
                        Flags = MouseFlags.ReportMousePosition,
                    }
                );
                list.Redraw(list.Bounds);
                Assert.True(PointerHighlight.IsOver(list));
                Assert.Equal(1, PointerHighlight.Row(list));
                Assert.Equal(0, list.SelectedItem);
                Assert.True(first.HasFocus);
                second.Enabled = false;
                Application.RootMouseEvent(mouse);
                Assert.False(PointerHighlight.IsOver(second));

                // Bottom navigation paints hover without taking keyboard focus.
                var hoverColor = turbo ? TerminalTheme.Menu.Focus : TerminalTheme.HomeAction.Focus;
                int bottom = driver.Rows - 1;
                Application.RootMouseEvent(
                    new MouseEvent
                    {
                        X = 5,
                        Y = bottom,
                        View = status,
                        Flags = MouseFlags.ReportMousePosition,
                    }
                );
                status.Redraw(status.Bounds);
                Assert.Equal((int)hoverColor, driver.Contents[bottom, 5, 1]);
                Assert.False(status.CanFocus);
                Assert.True(first.HasFocus);
                Application.RootKeyEvent(key);
                status.Redraw(status.Bounds);
                Assert.NotEqual((int)hoverColor, driver.Contents[bottom, 5, 1]);
                Application.RootMouseEvent(
                    new MouseEvent
                    {
                        X = 15,
                        Y = bottom,
                        View = status,
                        Flags = MouseFlags.ReportMousePosition,
                    }
                );
                status.Redraw(status.Bounds);
                Assert.Equal((int)status.ColorScheme.Disabled, driver.Contents[bottom, 15, 1]);
            }
            finally
            {
                Application.End(state);
            }
        }
        finally
        {
            Application.Shutdown();
        }
    }
}
