#nullable enable
using System;
using System.Collections.Generic;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

/// <summary>The two setup screens share sizing, focus order, and action hit areas.</summary>
internal sealed class SetupWorkspace : IDisposable
{
    public WorkspaceDialog Dialog { get; }
    public ScrollView Fields { get; }
    public TextField Target { get; }
    public List<View> Controls { get; } = new();
    public Dictionary<string, WorkspaceButton> Actions { get; } = new();
    public WorkspaceButton Cancel { get; }
    public WorkspaceButton Back { get; }
    private readonly Label status;
    private readonly TextView log;
    private readonly List<string> lines = new();
    private readonly InstallationCatalog catalog;

    public SetupWorkspace(InstallationCatalog catalog, string target, int fieldRows)
    {
        this.catalog = catalog;
        Dialog = new WorkspaceDialog(catalog.Product + " · Manage installation")
        { Width = Dim.Fill(1), Height = Dim.Fill(1), ColorScheme = TerminalTheme.Window };
        Fields = new ScrollView
        {
            X = 1, Y = 0, Width = Dim.Fill(1),
            Height = Dim.Function(() => Math.Min(fieldRows, Math.Max(4, Dialog.Bounds.Height - 12))),
            ContentSize = new Size(60, fieldRows), ShowVerticalScrollIndicator = true,
            ShowHorizontalScrollIndicator = false,
        };
        Fields.LayoutStarted += _ => Fields.ContentSize = new Size(Math.Max(1, Fields.Bounds.Width - 1), fieldRows);
        Dialog.Add(Fields);
        Target = Field(0, "Installation", target);
        var choose = new WorkspaceButton("Choose _installation…") { X = 0, Y = 2, Width = Dim.Fill(1), Height = 3 };
        choose.Clicked += () =>
        {
            string? picked = InstallationPicker.Show(catalog, Target.Text.ToString());
            if (picked is not null) { Target.Text = picked; Target.CursorPosition = 0; }
        };
        Fields.Add(choose);
        Controls.Add(choose);
        TrackFocus(choose);
        status = new Label("") { X = 1, Y = Pos.Bottom(Fields), Width = Dim.Fill(1), Height = 2 };
        Dialog.Add(status);
        foreach (var (action, label, column) in new[] { ("install", "_Install", 0), ("update", "_Update", 1), ("uninstall", "U_ninstall", 2) })
        {
            var button = Button(label, column, 0);
            Actions.Add(action, button);
            Controls.Add(button);
        }
        Actions.Add("check", Button("Check _prerequisites", 0, 3));
        Controls.Add(Actions["check"]);
        Cancel = Button("_Cancel task", 1, 3);
        Cancel.Enabled = false;
        Back = Button("_Back", 2, 3);
        var activity = new Label("Activity") { X = 1, Y = Pos.Bottom(Back), ColorScheme = TerminalTheme.Title };
        Dialog.Add(activity);
        log = new TextView
        { X = 1, Y = Pos.Bottom(activity), Width = Dim.Fill(1), Height = Dim.Fill(), ReadOnly = true, WordWrap = true, ColorScheme = TerminalTheme.HomeAction };
        Dialog.Add(log);
        Target.TextChanged += _ => Refresh();
        Refresh();
    }

    private WorkspaceButton Button(string label, int column, int row)
    {
        var button = new WorkspaceButton(label)
        { X = column == 0 ? 1 : Pos.Percent(column * 33), Y = Pos.Bottom(status) + row, Width = Dim.Percent(32), Height = 3 };
        Dialog.Add(button);
        return button;
    }

    public TextField Field(int row, string label, string value)
    {
        Fields.Add(new Label(label) { X = 0, Y = row, ColorScheme = TerminalTheme.Desktop });
        var field = new TextField(value) { X = 16, Y = row, Width = Dim.Fill(1), CursorPosition = 0 };
        Fields.Add(field);
        Controls.Add(field);
        TrackFocus(field);
        return field;
    }

    public void TrackFocus(View control)
    {
        control.Enter += _ =>
        {
            int top = -Fields.ContentOffset.Y;
            int bottom = control.Frame.Y + control.Frame.Height;
            if (control.Frame.Y < top) top = control.Frame.Y;
            else if (bottom > top + Fields.Bounds.Height) top = bottom - Fields.Bounds.Height;
            Fields.ContentOffset = new Point(0, -Math.Max(0, top));
        };
    }

    public void Append(string message)
    {
        lines.Add(message);
        if (lines.Count > 500) lines.RemoveAt(0);
        log.Text = string.Join('\n', lines);
        log.MoveEnd();
    }

    public void SetBusy(bool busy)
    {
        foreach (var control in Controls) control.Enabled = !busy;
        Cancel.Enabled = busy;
        if (!busy) Refresh();
    }

    public void Refresh()
    {
        try
        {
            var item = catalog.Inspect(Target.Text.ToString() ?? "");
            status.Text = item.Status;
            Actions["install"].Enabled = item.CanInstall;
            Actions["update"].Enabled = item.CanUpdate;
            Actions["uninstall"].Enabled = item.CanUninstall;
        }
        catch (Exception error) when (InstallationCatalog.IsPathError(error))
        {
            status.Text = error.Message;
            foreach (string action in new[] { "install", "update", "uninstall" }) Actions[action].Enabled = false;
        }
    }

    public void Remember()
    {
        try { catalog.Remember(Target.Text.ToString() ?? ""); }
        catch (Exception error) when (InstallationCatalog.IsPathError(error)) { Append("Could not remember installation: " + error.Message); }
    }

    public void Dispose() => Dialog.Dispose();
}
