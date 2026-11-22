using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;

namespace musicApp.Hotkey
{
    public class LocalHotkeys
    {
        private readonly MainWindow _mainWindow;
        private Dictionary<ShortcutGesture, ShortcutAction> _lookup = new();

        public LocalHotkeys(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.PreviewKeyDown += MainWindow_PreviewKeyDown;
            Reload();
        }

        public void Reload()
        {
            var prefs = PreferencesManager.Instance.LoadPreferencesSync();
            PreferencesManager.EnsureInitialized(prefs);
            _lookup = ShortcutCatalog.BuildLookup(prefs.Shortcuts.Bindings);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Leave typing and slider keys alone
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is Slider)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!_lookup.TryGetValue(new ShortcutGesture(key, Keyboard.Modifiers), out var action))
                return;

            // Held keys only repeat for volume and queue moves
            if (e.IsRepeat && !ShortcutCatalog.Get(action).AllowRepeat)
                return;

            e.Handled = _mainWindow.RunShortcut(action);
        }
    }
}
