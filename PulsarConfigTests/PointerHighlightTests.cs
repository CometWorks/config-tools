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
            top.Add(first, second, list);
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
