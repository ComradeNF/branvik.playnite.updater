using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Branvik.EmulatorUpdater
{
    public class Configuration { public List<Installation> Items { get; set; } = new List<Installation>(); }
    public class Installation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
        public string Repository { get; set; }
        public string InstalledTag { get; set; }
        public bool IncludePrereleases { get; set; }
        [System.Web.Script.Serialization.ScriptIgnore]
        public UpdateChecker.Release CheckedRelease { get; set; }
        public string Status { get; set; }
        public string ReleaseUrl { get; set; }
    }
    public class UpdaterPlugin : GenericPlugin
    {
        public override Guid Id { get; } = Guid.Parse("90ce6139-25c7-4771-9728-47ca18a87a1c");
        internal Configuration Config;
        private Window window;
        public UpdaterPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties();
            Config = LoadPluginSettings<Configuration>() ?? new Configuration();
            if (Config.Items == null) Config.Items = new List<Installation>();
            // Results are session-only; do not show stale or previously localized messages.
            foreach (var item in Config.Items)
            {
                item.Status = null;
                item.ReleaseUrl = null;
                item.CheckedRelease = null;
            }
        }
        internal void Save() { SavePluginSettings(Config); }
        internal void Sync()
        {
            var emulators = PlayniteApi.Database.Emulators.ToList();
            var ids = new HashSet<string>(emulators.Select(e => e.Id.ToString()));
            Config.Items.RemoveAll(x => !x.Id.StartsWith("manual:") && !ids.Contains(x.Id));
            foreach (var emulator in emulators)
            {
                var item = Config.Items.FirstOrDefault(x => x.Id == emulator.Id.ToString());
                if (item == null) { item = new Installation { Id = emulator.Id.ToString() }; Config.Items.Add(item); }
                item.Name = emulator.Name;
                item.Folder = emulator.InstallDir;
            }
            Save();
        }
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            var icon = new Path
            {
                Data = Geometry.Parse("M 10,2 L 10,13 M 5,8 L 10,13 L 15,8 M 3,13 L 3,18 L 17,18 L 17,13"),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                UseLayoutRounding = true
            };
            // Inherit the host's foreground, including its theme-defined hover state.
            icon.SetBinding(Shape.StrokeProperty, new Binding
            {
                Path = new PropertyPath(TextElement.ForegroundProperty),
                RelativeSource = RelativeSource.Self
            });
            yield return new TopPanelItem
            {
                Title = "Branvik Emulator Updater",
                Icon = icon,
                Visible = true,
                Activated = OpenUpdater
            };
        }
        private void OpenUpdater()
        {
                try
                {
                    if (window != null)
                    {
                        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                        window.Activate();
                        return;
                    }
                    Sync();
                    var themedWindow = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });
                    themedWindow.Title = "Branvik Emulator Updater";
                    themedWindow.Width = 1100;
                    themedWindow.Height = 550;
                    themedWindow.MinWidth = 700;
                    themedWindow.MinHeight = 350;
                    themedWindow.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                    themedWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    themedWindow.Content = new UpdaterWindow(this, PlayniteApi, themedWindow);
                    window = themedWindow;
                    window.Closed += (s, e) => window = null;
                    window.Show();
                }
                catch (Exception ex) { PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Branvik Emulator Updater"); }
        }
    }
}
