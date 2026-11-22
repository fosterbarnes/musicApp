using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace musicApp.Hotkey
{
    public enum ShortcutAction
    {
        PlayPause,
        PreviousTrack,
        NextTrack,
        PlaySelected,
        PlayNext,
        AddToQueue,
        RemoveFromQueue,
        MoveQueueItemUp,
        MoveQueueItemDown,
        UndoQueueReplace,
        ToggleShuffle,
        CycleRepeat,
        VolumeUp,
        VolumeDown,
        ToggleMute
    }

    public readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers)
    {
        private static readonly KeyConverter KeyNames = new();

        private static readonly (ModifierKeys Flag, string Name)[] ModifierNames =
        {
            (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Alt, "Alt")
        };

        public static ShortcutGesture? TryParse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            var mods = ModifierKeys.None;
            foreach (var part in parts[..^1])
            {
                var match = ModifierNames.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
                if (match.Name == null)
                    return null;
                mods |= match.Flag;
            }

            try
            {
                return KeyNames.ConvertFromInvariantString(parts[^1]) is Key key && key != Key.None
                    ? new ShortcutGesture(key, mods)
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Saved form, e.g. "Ctrl+Shift+Q"
        public override string ToString() => Join(KeyNames.ConvertToInvariantString(Key) ?? Key.ToString());

        // Settings table form: arrows and Enter as symbols
        public string ToDisplayString() => Join(Key switch
        {
            Key.Space => "Space",
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Enter => "Enter",
            Key.Delete => "Del",
            _ => KeyNames.ConvertToInvariantString(Key) ?? Key.ToString()
        });

        private string Join(string keyName)
        {
            var mods = Modifiers;
            return string.Concat(ModifierNames.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Name + "+")) + keyName;
        }
    }

    public sealed record ShortcutDefinition(ShortcutAction Action, string Name, ShortcutGesture Default, bool AllowRepeat = false);

    public static class ShortcutCatalog
    {
        public static IReadOnlyList<ShortcutDefinition> All { get; } = new ShortcutDefinition[]
        {
            new(ShortcutAction.PlayPause, "Play/Pause", new(Key.Space, ModifierKeys.None)),
            new(ShortcutAction.PreviousTrack, "Previous Track", new(Key.Left, ModifierKeys.None)),
            new(ShortcutAction.NextTrack, "Next Track", new(Key.Right, ModifierKeys.None)),
            new(ShortcutAction.PlaySelected, "Play Selected Track", new(Key.Enter, ModifierKeys.None)),
            new(ShortcutAction.PlayNext, "Play Next", new(Key.Enter, ModifierKeys.Shift)),
            new(ShortcutAction.AddToQueue, "Add to Queue", new(Key.Q, ModifierKeys.Control)),
            new(ShortcutAction.RemoveFromQueue, "Remove from Queue", new(Key.Delete, ModifierKeys.None)),
            new(ShortcutAction.MoveQueueItemUp, "Move Up in Queue", new(Key.Up, ModifierKeys.Alt), AllowRepeat: true),
            new(ShortcutAction.MoveQueueItemDown, "Move Down in Queue", new(Key.Down, ModifierKeys.Alt), AllowRepeat: true),
            new(ShortcutAction.UndoQueueReplace, "Undo Queue Replace", new(Key.Z, ModifierKeys.Control)),
            new(ShortcutAction.ToggleShuffle, "Shuffle On/Off", new(Key.S, ModifierKeys.Control)),
            new(ShortcutAction.CycleRepeat, "Repeat Mode", new(Key.R, ModifierKeys.Control)),
            new(ShortcutAction.VolumeUp, "Volume Up", new(Key.Up, ModifierKeys.Control), AllowRepeat: true),
            new(ShortcutAction.VolumeDown, "Volume Down", new(Key.Down, ModifierKeys.Control), AllowRepeat: true),
            new(ShortcutAction.ToggleMute, "Mute/Unmute", new(Key.M, ModifierKeys.None))
        };

        public static ShortcutDefinition Get(ShortcutAction action) => All.First(d => d.Action == action);

        // Missing entry = default, empty string = unbound
        public static ShortcutGesture? Resolve(ShortcutAction action, IReadOnlyDictionary<string, string>? overrides) =>
            overrides != null && overrides.TryGetValue(action.ToString(), out var text)
                ? ShortcutGesture.TryParse(text)
                : Get(action).Default;

        public static Dictionary<ShortcutGesture, ShortcutAction> BuildLookup(IReadOnlyDictionary<string, string>? overrides)
        {
            var map = new Dictionary<ShortcutGesture, ShortcutAction>();
            foreach (var def in All)
            {
                if (Resolve(def.Action, overrides) is { } g)
                    map.TryAdd(g, def.Action);
            }
            return map;
        }

        public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
            or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

        // Keys the app or Windows needs for itself
        public static bool IsReserved(ShortcutGesture g) =>
            g.Key is Key.Tab or Key.Escape or Key.LWin or Key.RWin
            || (g.Modifiers == ModifierKeys.None && g.Key is Key.Up or Key.Down)
            || (g.Modifiers == ModifierKeys.Alt && g.Key == Key.F4)
            || g.Modifiers.HasFlag(ModifierKeys.Windows);
    }
}
