using System;
using System.Collections.Generic;
using System.ComponentModel;
using musicApp.Hotkey;

namespace musicApp;

public sealed class KeyboardShortcutRow : INotifyPropertyChanged
{
    private string _shortcut = "";
    private bool _isCapturing;

    // Null for fixed rows (built-in list navigation)
    public ShortcutAction? Action { get; init; }
    public string Name { get; init; } = "";
    public bool IsEditable => Action != null;

    public string Shortcut
    {
        get => _shortcut;
        set { _shortcut = value; Notify(nameof(Shortcut)); }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; Notify(nameof(IsCapturing)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<KeyboardShortcutRow> FixedInApp { get; } = new[]
    {
        new KeyboardShortcutRow { Shortcut = "↑", Name = "Navigate Up" },
        new KeyboardShortcutRow { Shortcut = "↓", Name = "Navigate Down" }
    };

    public static IReadOnlyList<KeyboardShortcutRow> Global { get; } =
        Array.Empty<KeyboardShortcutRow>();
}
