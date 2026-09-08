#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Magnetar.Config.Ui;
using Terminal.Gui;

namespace CometWorks.ConfigTools;

internal static class InstallationPicker
{
    public static string? Show(InstallationCatalog catalog, string? selected = null, bool allowNew = true)
    {
        using var dialog = new WorkspaceDialog(catalog.Product + " · Installations")
        { Width = Dim.Fill(1), Height = Dim.Fill(1), ColorScheme = TerminalTheme.Window };
        var list = new WorkspaceList { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(13) };
        var detail = new Label("") { X = 1, Y = Pos.AnchorEnd(12), Width = Dim.Fill(1), Height = 2 };
        var path = new TextField(selected ?? "") { X = 1, Y = Pos.AnchorEnd(10), Width = Dim.Fill(1) };
        var note = new Label("Select an installation or enter its folder above.")
        { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Height = 2 };
        dialog.Add(list, detail, path);
        var found = new List<Installation>();
        string? result = null;
        bool busy = false, leaving = false;
        CancellationTokenSource? cancellation = null;
        var controls = new List<View> { list, path };
        void Refresh(IEnumerable<Installation>? extra = null)
        {
            string keep = path.Text.ToString() ?? "";
            found = catalog.Discover(selected).Concat(extra ?? []).DistinctBy(item => item.Path, InstallationCatalog.PathComparer).ToList();
            list.SetSource(found);
            int index = found.FindIndex(item => InstallationCatalog.PathComparer.Equals(item.Path, keep));
            if (index >= 0) list.SelectedItem = index;
            // Refresh must not replace a manually entered or explicit target.
            path.Text = keep;
            detail.Text = found.Count == 0 ? "No installations found. Browse or search a folder." : $"{found.Count} known location(s).";
        }
        list.SelectedItemChanged += args =>
        {
            if (args.Item < 0 || args.Item >= found.Count) return;
            var item = found[args.Item];
            path.Text = item.Path;
            path.CursorPosition = 0;
            detail.Text = item.Status;
        };
        void Choose(bool newInstall)
        {
            try
            {
                var item = catalog.Inspect(path.Text.ToString() ?? "");
                if (newInstall ? !item.CanInstall : allowNew
                        ? item.Kind is not (InstallationKind.Current or InstallationKind.Older or InstallationKind.Incomplete)
                        : !item.CanOpen)
                {
                    note.Text = newInstall ? "Choose a new, empty folder or a previously uninstalled target." : item.Status;
                    return;
                }
                try { catalog.Remember(item.Path); }
                catch (Exception error) when (InstallationCatalog.IsPathError(error))
                { MessageBox.ErrorQuery("Installation history", "Could not remember this folder: " + error.Message, "OK"); }
                result = item.Path;
                Application.RequestStop(dialog);
            }
            catch (Exception error) when (InstallationCatalog.IsPathError(error)) { note.Text = error.Message; }
        }
        WorkspaceButton Action(string text, int column, int row, Action clicked, int columns = 3)
        {
            var button = new WorkspaceButton(text)
            { X = column == 0 ? 1 : Pos.Percent(column * (100 / columns)), Y = Pos.AnchorEnd(row), Width = Dim.Percent(100 / columns - 1), Height = 3 };
            button.Clicked += clicked;
            dialog.Add(button);
            controls.Add(button);
            return button;
        }
        Action("_Use folder", 0, 9, () => Choose(false), 4);
        Action("_Browse…", 1, 9, () =>
        {
            string? picked = FileDialogs.PickDirectory("Installation folder", "Select the folder containing the launchers", path.Text.ToString());
            if (picked is null) return;
            path.Text = picked;
            path.CursorPosition = 0;
            try { detail.Text = catalog.Inspect(picked).Status; }
            catch (Exception error) when (InstallationCatalog.IsPathError(error)) { note.Text = error.Message; }
        }, 4);
        Action("_Refresh", 2, 9, () => Refresh(), 4);
        Action("_Forget", 3, 9, () =>
        {
            try
            {
                catalog.Forget(path.Text.ToString() ?? "");
                Refresh();
                note.Text = "Removed from saved history; files kept. Default/receipt locations can still appear.";
            }
            catch (Exception error) when (InstallationCatalog.IsPathError(error)) { note.Text = error.Message; }
        }, 4);
        Action("_Search folder…", 0, 6, async () =>
        {
            string? root = FileDialogs.PickDirectory("Search installations", "Search up to 4 levels and 2,000 directories", path.Text.ToString());
            if (root is null) return;
            busy = true;
            cancellation = new CancellationTokenSource();
            foreach (var control in controls) control.Enabled = false;
            note.Text = "Searching… Back cancels the search.";
            try
            {
                var token = cancellation.Token;
                var search = await Task.Run(() =>
                {
                    var items = catalog.Search(root, token, out bool limited);
                    return (items, limited);
                });
                Application.MainLoop.Invoke(() =>
                {
                    Refresh(search.items);
                    note.Text = search.limited ? "Search limit reached. Search a narrower folder for more results." : $"Found {search.items.Count} installation(s) in search.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Application.MainLoop.Invoke(() => note.Text = "Search stopped: " + error.Message); }
            finally
            {
                Application.MainLoop.Invoke(() =>
                {
                    busy = false;
                    cancellation.Dispose();
                    cancellation = null;
                    foreach (var control in controls) control.Enabled = true;
                    if (leaving) Application.RequestStop(dialog);
                });
            }
        });
        if (allowNew) Action("Install _new…", 1, 6, () => Choose(true));
        var back = Action("_Back", 2, 6, () => Application.RequestStop(dialog));
        controls.Remove(back);
        dialog.Add(note);
        dialog.Closing += args =>
        {
            if (!busy) return;
            args.Cancel = true;
            leaving = true;
            cancellation?.Cancel();
        };
        list.OpenSelectedItem += args =>
        {
            if (args.Item < 0 || args.Item >= found.Count) return;
            path.Text = found[args.Item].Path;
            Choose(false);
        };
        Refresh();
        if (string.IsNullOrWhiteSpace(selected) && found.Count > 0) path.Text = found[0].Path;
        Application.Run(dialog);
        return result;
    }
}
