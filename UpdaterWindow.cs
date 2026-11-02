using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;

namespace Branvik.EmulatorUpdater
{
    public class UpdaterWindow : UserControl
    {
        private readonly UpdaterPlugin plugin;
        private readonly IPlayniteAPI api;
        private readonly DataGrid grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow
        };
        private readonly DockPanel actions = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        private readonly StackPanel primaryActions = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly StackPanel secondaryActions = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool installing;
        public UpdaterWindow(UpdaterPlugin plugin, IPlayniteAPI api, Window window)
        {
            this.plugin = plugin; this.api = api;
            // Use the active Playnite theme instead of WPF's default text style.
            SetResourceReference(Control.ForegroundProperty, "TextBrush");
            Resources[typeof(TextBlock)] = new Style(typeof(TextBlock))
            {
                BasedOn = (Style)Application.Current.FindResource("BaseTextBlockStyle")
            };
            var panel = new DockPanel { Margin = new Thickness(12) }; Content = panel;
            var hint = new TextBlock { Text = "Enter a GitHub repository (owner/repo) and the installed release tag. Changes are saved automatically.\nEnable prereleases to include nightly builds published on GitHub Releases. The newest by publication date among the 100 most recent releases is selected.", Margin = new Thickness(0,0,0,10), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
            DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
            DockPanel.SetDock(primaryActions, Dock.Left); actions.Children.Add(primaryActions);
            DockPanel.SetDock(secondaryActions, Dock.Right); actions.Children.Add(secondaryActions);
            AddColumn("Emulator", "Name", true); AddColumn("Folder", "Folder", true); AddColumn("GitHub owner/repo", "Repository", false);
            AddColumn("Installed tag", "InstalledTag", false);
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Include\nprereleases",
                Binding = EditBinding("IncludePrereleases"),
                Width = DataGridLength.Auto
            });
            AddColumn("Status", "Status", true);
            panel.Children.Add(grid); Refresh();
            grid.SourceUpdated += (s, e) =>
            {
                try { plugin.Save(); }
                catch (Exception ex) { api.Dialogs.ShowErrorMessage("Could not save changes: " + ex.Message); }
            };
            IconButton(primaryActions, "Synchronize", "M 3,8 A 7,7 0 0 1 16,5 M 16,1 L 16,5 L 12,5 M 17,12 A 7,7 0 0 1 4,15 M 4,19 L 4,15 L 8,15",
                () => { Commit(); plugin.Sync(); Refresh(); });
            IconButton(secondaryActions, "Add emulator manually", "M 10,2 L 10,18 M 2,10 L 18,10", () =>
            {
                var folder = api.Dialogs.SelectFolder(); if (string.IsNullOrEmpty(folder)) return;
                if (plugin.Config.Items.Any(x => string.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase))) return;
                plugin.Config.Items.Add(new Installation { Id = "manual:" + Guid.NewGuid(), Folder = folder, Name = System.IO.Path.GetFileName(folder.TrimEnd('\\')) }); plugin.Save(); Refresh();
            });
            var remove = IconButton(secondaryActions, "Remove emulator", "M 3,5 L 17,5 M 7,5 L 7,2 L 13,2 L 13,5 M 5,5 L 6,18 L 14,18 L 15,5 M 8,8 L 8,15 M 12,8 L 12,15", () =>
            {
                var selected = grid.SelectedItems.Cast<Installation>().ToList();
                if (selected.Count == 0 || selected.Any(item => item.Id == null || !item.Id.StartsWith("manual:"))) return;
                Commit();
                foreach (var item in selected) plugin.Config.Items.Remove(item);
                plugin.Save(); Refresh();
            });
            Action updateRemove = () => remove.IsEnabled = grid.SelectedItems.Count > 0 &&
                grid.SelectedItems.Cast<Installation>().All(item => item.Id != null && item.Id.StartsWith("manual:"));
            grid.SelectionChanged += (s, e) => updateRemove();
            updateRemove();
            var check = IconButton(primaryActions, "Check for updates", "M 13,8 A 5,5 0 1 1 3,8 A 5,5 0 1 1 13,8 M 12,12 L 18,18");
            BindSelection(check);
            check.Click += async (s, e) =>
            {
                try
                {
                    Commit();
                    var selected = grid.SelectedItems.Cast<Installation>().ToList();
                    if (selected.Count == 0) return;
                    actions.IsEnabled = false; grid.IsReadOnly = true;
                    foreach (var item in selected)
                    {
                        try { await UpdateChecker.Check(item, cancellation.Token); }
                        catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) break; item.Status = "Request timed out"; }
                        catch (Exception ex) { item.Status = "Error: " + ex.Message; }
                        grid.Items.Refresh();
                    }
                }
                catch (Exception ex) { api.Dialogs.ShowErrorMessage(ex.Message, "Check for updates"); }
                finally { actions.IsEnabled = true; grid.IsReadOnly = false; }
            };
            var install = IconButton(primaryActions, "Update", "M 10,2 L 10,13 M 5,8 L 10,13 L 15,8 M 3,13 L 3,18 L 17,18 L 17,13");
            BindSelection(install);
            install.Click += async (s, e) =>
            {
                try
                {
                    Commit();
                    // Snapshot the selection; later UI selection changes must not change the batch.
                    var selectedItems = grid.SelectedItems.Cast<Installation>().ToList();
                    if (selectedItems.Count == 0) return;
                    var results = new List<string>();
                    installing = true; actions.IsEnabled = false; grid.IsEnabled = false;
                    foreach (var item in selectedItems)
                    {
                        try
                        {
                        string executable = null;
                        var generic = !UpdateInstaller.Supported(item.Repository);
                        if (generic)
                        {
                            executable = Choose("Select the installed emulator executable", System.IO.Directory.GetFiles(item.Folder, "*.exe").Select(System.IO.Path.GetFileName).ToList());
                            if (executable == null) continue;
                        }
                        UpdateInstaller.ValidateFolder(item.Folder, item.Repository, executable);
                        item.Status = "Fetching release information…"; grid.Items.Refresh();
                        await UpdateChecker.Check(item, cancellation.Token);
                        if (item.CheckedRelease == null) throw new InvalidOperationException(item.Status);
                        var release = item.CheckedRelease;
                        UpdateChecker.Asset asset;
                        if (generic)
                        {
                            var candidates = (release.assets ?? new List<UpdateChecker.Asset>()).Where(a =>
                                a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || a.name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)).ToList();
                            var chosen = Choose("Select a portable Windows archive for your CPU", candidates.Select(a => a.name).ToList());
                            if (chosen == null) continue;
                            asset = candidates.Single(a => a.name == chosen);
                        }
                        else asset = UpdateInstaller.SelectAsset(release, item.Repository);
                        if (api.Dialogs.ShowMessage("Emulator: " + item.Name + "\nInstall " + release.tag_name + "?\nArchive: " + asset.name +
                            "\nFolder: " + item.Folder + "\nClose the emulator. Files to be replaced will be backed up in BranvikBackups next to the emulator folder.\n" +
                            (string.IsNullOrEmpty(asset.digest) ? "GitHub did not provide a checksum.\n" : "The SHA-256 checksum will be verified.\n") +
                            (generic ? "Generic mode replaces EXE/DLL/PDB files and adds missing files. Existing data files are preserved. Some emulators may require manual updates to other resources." : "Settings, firmware, BIOS, memory cards and saves will be preserved."), "Update emulator", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        {
                            item.Status = "Skipped by user";
                            results.Add(item.Name + ": " + item.Status);
                            continue;
                        }
                        item.Status = "Downloading and installing…"; grid.Items.Refresh();
                        var backup = await UpdateInstaller.Install(item.Folder, asset, cancellation.Token, item.Repository, executable);
                        item.InstalledTag = release.tag_name;
                        item.Status = "Installed " + release.tag_name;
                        try { plugin.Save(); }
                        catch (Exception saveError) { api.Dialogs.ShowErrorMessage("The update was installed, but its tag could not be saved: " + saveError.Message); }
                        results.Add(item.Name + ": " + item.Status + "\nBackup: " + backup);
                        }
                        catch (Exception ex)
                        {
                            item.Status = "Update failed: " + ex.Message;
                            results.Add(item.Name + ": " + item.Status);
                        }
                        finally { grid.Items.Refresh(); }
                    }
                    api.Dialogs.ShowMessage(string.Join("\n\n", results), "Update results");
                }
                catch (Exception ex)
                {
                    api.Dialogs.ShowErrorMessage(ex.Message, "Update failed");
                }
                finally { installing = false; actions.IsEnabled = true; grid.IsEnabled = true; grid.Items.Refresh(); }
            };
            window.Closing += (s, e) => { if (installing) { e.Cancel = true; return; } try { Commit(); } catch (Exception ex) { e.Cancel = true; api.Dialogs.ShowErrorMessage(ex.Message); } };
            window.Closed += (s, e) => cancellation.Cancel();
        }
        private void Commit() { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); plugin.Save(); }
        private string Choose(string title, List<string> choices)
        {
            if (choices.Count == 0) throw new InvalidOperationException("No compatible files found. Only portable ZIP/7z releases and root-level Windows executables are supported.");
            var dialog = api.Dialogs.CreateWindow(new WindowCreationOptions());
            dialog.Title = title; dialog.Width = 650; dialog.Height = 350;
            dialog.Owner = Window.GetWindow(this); dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new DockPanel { Margin = new Thickness(16) };
            var list = new ListBox { ItemsSource = choices };
            var confirm = new Button { Content = "Select", Margin = new Thickness(0,12,0,0), IsEnabled = false };
            list.SelectionChanged += (s,e) => confirm.IsEnabled = list.SelectedItem != null;
            confirm.Click += (s,e) => dialog.DialogResult = true;
            DockPanel.SetDock(confirm, Dock.Bottom); panel.Children.Add(confirm); panel.Children.Add(list);
            dialog.Content = panel;
            return dialog.ShowDialog() == true ? list.SelectedItem as string : null;
        }
        private sealed class HasSelectionConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is int && (int)value > 0;
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => Binding.DoNothing;
        }
        private void Refresh() { grid.ItemsSource = null; grid.ItemsSource = plugin.Config.Items; }
        private static Binding EditBinding(string property) => new Binding(property)
        {
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, NotifyOnSourceUpdated = true
        };
        private void AddColumn(string title, string property, bool readOnly) { grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = readOnly ? new Binding(property) : EditBinding(property), IsReadOnly = readOnly, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); }
        private void BindSelection(Button button) => button.SetBinding(IsEnabledProperty,
            new Binding("SelectedItems.Count") { Source = grid, Converter = new HasSelectionConverter() });
        private Button IconButton(Panel parent, string title, string geometry, Action action = null)
        {
            var icon = new Path { Data = Geometry.Parse(geometry), Width = 20, Height = 20,
                Stretch = Stretch.Uniform, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            // Let the active theme measure its border and content chrome; fixed height
            // previously left less room than the icon plus padding required.
            var button = new Button { Content = icon, ToolTip = title, MinWidth = 40, MinHeight = 40,
                Margin = new Thickness(3), Padding = new Thickness(6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, UseLayoutRounding = true };
            icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { Source = button });
            AutomationProperties.SetName(button, title);
            ToolTipService.SetShowOnDisabled(button, true);
            parent.Children.Add(button);
            if (action != null) button.Click += (s, e) => { try { action(); } catch (Exception ex) { api.Dialogs.ShowErrorMessage(ex.Message, "Branvik Emulator Updater"); } };
            return button;
        }
    }
}
