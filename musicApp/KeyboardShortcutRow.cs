using System;
using System.Collections.Generic;

namespace musicApp;

public sealed class KeyboardShortcutRow
{
    public string Shortcut { get; init; } = "";
    public string Action { get; init; } = "";
}

public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<KeyboardShortcutRow> InApp { get; } =
        Array.Empty<KeyboardShortcutRow>();

    public static IReadOnlyList<KeyboardShortcutRow> Global { get; } =
        Array.Empty<KeyboardShortcutRow>();
}
