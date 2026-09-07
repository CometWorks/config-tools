# Terminal.Gui 1.19 input queue patch

The bundled v1.19 NetDriver shares two `Queue<InputResult?>` instances between
input, resize, mouse-repeat and UI threads without synchronization. A real
PulsarConfig 1.0.0 dump showed `InvalidOperationException: Queue empty` in
`NetMainLoop.NetInputHandler` at `Peek()`. Its input task faults silently and
menus, mouse and keyboard stop responding.

This build-only patch replaces both queues with `ConcurrentQueue<T>`. The input
producer filters null shutdown events before enqueueing instead of peeking and
removing them from the UI consumer's queue. Each queue has one consumer;
`TryDequeue` is used when draining. Input decoding and key/mouse mappings stay
in the original library.

`Directory.Build.targets` patches a private copy for builds, tests and published
single-file bundles. The NuGet cache is unchanged. Mono.Cecil and the patcher
are build dependencies only and are not shipped. The original library's MIT
notice is included with each release.

The patch checks the exact SHA-256 of NuGet's net8.0 Terminal.Gui 1.19.0 DLL and
the old loop shape, and fails closed on a different dependency. Reassess/remove
this patch when upgrading the library; do not just change the expected hash.

Upstream source:
https://github.com/gui-cs/Terminal.Gui/blob/v1.19.0/Terminal.Gui/ConsoleDrivers/NetDriver.cs

Regression checks: `TerminalInputTests` exercises the bundled queues under
concurrent production; `Scripts/test-terminal-input.py` drives a published tool
through a real Linux PTY with bursts of mouse/menu input and checks keyboard
navigation afterwards.
