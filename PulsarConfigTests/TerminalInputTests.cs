#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Terminal.Gui;
using Xunit;

namespace Pulsar.Config.Tests;

public class TerminalInputTests
{
    [Fact]
    public async Task Bundled_input_queues_support_concurrent_producers_and_ignore_shutdown_events()
    {
        var assembly = typeof(Application).Assembly;
        foreach (string name in new[] { "Terminal.Gui.NetEvents", "Terminal.Gui.NetMainLoop" })
        {
            var field = assembly
                .GetType(name)!
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(f => f.Name is "inputResultQueue" or "inputResult");
            Assert.Equal(typeof(ConcurrentQueue<>), field.FieldType.GetGenericTypeDefinition());
        }
        var helper = assembly.GetType("Terminal.Gui.ConfigToolsInputQueue")!;
        var add = helper
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(int?));
        var take = helper
            .GetMethod("Take", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(int?));
        var queue = new ConcurrentQueue<int?>();
        add.Invoke(null, [queue, null]);
        Assert.Empty(queue);
        var producer = Task.Run(() => Parallel.For(0, 10000, i => add.Invoke(null, [queue, i])));
        var received = new HashSet<int>();
        while (!producer.IsCompleted || !queue.IsEmpty)
        {
            if (take.Invoke(null, [queue]) is int value)
                Assert.True(received.Add(value), "Duplicate input event");
        }
        await producer;
        Assert.Equal(10000, received.Count);
    }
}
