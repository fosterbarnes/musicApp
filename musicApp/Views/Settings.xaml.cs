using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using MusicApp;
using MusicApp.Helpers;

namespace MusicApp.Views
{
    public partial class SettingsView : Window
    {
        public SettingsView()
        {
            InitializeComponent();
            KeyboardShortcutsInAppListView.ItemsSource = KeyboardShortcutCatalog.InApp;
            KeyboardShortcutsGlobalListView.ItemsSource = KeyboardShortcutCatalog.Global;
            KeyboardShortcutsInAppListView.SizeChanged += (_, _) => BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            KeyboardShortcutsGlobalListView.SizeChanged += (_, _) => BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);
            Loaded += SettingsView_Loaded;
            ShowSection("General");
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            AboutVersionSubtitleText.Text = AppVersionFiles.GetAboutVersionSubtitle();
            BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);

            try
            {
                var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                AboutInstallLocationText.Text = string.IsNullOrEmpty(baseDir) ? "—" : baseDir;
            }
            catch
            {
                AboutInstallLocationText.Text = "—";
            }

            try
            {
                AboutSettingsLocationText.Text = LibraryManager.SettingsDirectoryPath;
            }
            catch
            {
                AboutSettingsLocationText.Text = "—";
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future settings persistence logic.
        }

        private void AboutHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private static void BalanceKeyboardShortcutColumns(ListView listView)
        {
            if (listView.View is not GridView gv || gv.Columns.Count < 2)
            {
                return;
            }

            var w = listView.ActualWidth;
            if (w <= 0 || double.IsNaN(w))
            {
                return;
            }

            var colW = Math.Max(40, (w - 8) / 2.0);
            gv.Columns[0].Width = colW;
            gv.Columns[1].Width = colW;
        }

        private void BalanceAllKeyboardShortcutColumns()
        {
            BalanceKeyboardShortcutColumns(KeyboardShortcutsInAppListView);
            BalanceKeyboardShortcutColumns(KeyboardShortcutsGlobalListView);
        }

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string sectionName)
            {
                return;
            }

            ShowSection(sectionName);
        }

        private void ShowSection(string sectionName)
        {
            GeneralSectionPanel.Visibility = sectionName == "General" ? Visibility.Visible : Visibility.Collapsed;
            PlaybackSectionPanel.Visibility = sectionName == "Playback" ? Visibility.Visible : Visibility.Collapsed;
            LibrarySectionPanel.Visibility = sectionName == "Library" ? Visibility.Visible : Visibility.Collapsed;
            ThemeSectionPanel.Visibility = sectionName == "Theme" ? Visibility.Visible : Visibility.Collapsed;
            KeyboardShortcutsSectionPanel.Visibility = sectionName == "KeyboardShortcuts" ? Visibility.Visible : Visibility.Collapsed;
            AboutSectionPanel.Visibility = sectionName == "About" ? Visibility.Visible : Visibility.Collapsed;

            if (sectionName == "KeyboardShortcuts")
            {
                Dispatcher.BeginInvoke(new Action(BalanceAllKeyboardShortcutColumns), DispatcherPriority.Loaded);
            }

            SetSectionButtonState(GeneralSectionButton, sectionName == "General");
            SetSectionButtonState(PlaybackSectionButton, sectionName == "Playback");
            SetSectionButtonState(LibrarySectionButton, sectionName == "Library");
            SetSectionButtonState(ThemeSectionButton, sectionName == "Theme");
            SetSectionButtonState(KeyboardShortcutsSectionButton, sectionName == "KeyboardShortcuts");
            SetSectionButtonState(AboutSectionButton, sectionName == "About");
        }

        private void SetSectionButtonState(Button button, bool isActive)
        {
            var isLast = ReferenceEquals(button, AboutSectionButton);
            var key = (isActive, isLast) switch
            {
                (true, true) => "SectionSegmentActiveLastStyle",
                (true, false) => "SectionSegmentActiveStyle",
                (false, true) => "SectionSegmentInactiveLastStyle",
                (false, false) => "SectionSegmentInactiveStyle",
            };

            if (TryFindResource(key) is Style style)
            {
                button.Style = style;
            }
        }
    }
}
