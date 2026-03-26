namespace musicApp.Helpers;

internal readonly struct LaunchParseResult
{
    internal bool OpenSettings { get; init; }
    internal bool OpenInfo { get; init; }
    internal string? SettingsSection { get; init; }
    internal string? InfoSection { get; init; }
}

internal static class LaunchArgsParser
{
    private static readonly (string Long, string Short, string Section)[] SettingsSectionFlags =
    {
        ("--settings_general", "--sg", "General"),
        ("--settings_playback", "--sp", "Playback"),
        ("--settings_library", "--sl", "Library"),
        ("--settings_shortcuts", "--ss", "KeyboardShortcuts"),
        ("--settings_theme", "--st", "Theme"),
        ("--settings_about", "--sa", "About"),
    };

    private static readonly (string Long, string Short, string Section)[] InfoSectionFlags =
    {
        ("--info_details", "--id", "Details"),
        ("--info_artwork", "--ia", "Artwork"),
        ("--info_lyrics", "--il", "Lyrics"),
        ("--info_options", "--io", "Options"),
        ("--info_sorting", "--is", "Sorting"),
        ("--info_file", "--if", "File"),
    };

    internal static LaunchParseResult Parse(string[]? args)
    {
        if (args == null || args.Length == 0)
            return default;

        var openSettings = false;
        var openInfo = false;
        string? settingsSection = null;
        string? infoSection = null;

        foreach (var raw in args)
        {
            if (string.IsNullOrEmpty(raw))
                continue;
            if (Matches(raw, "--settings", "--s"))
                openSettings = true;
            else if (Matches(raw, "--info", "--i"))
                openInfo = true;
            else if (TryMatchSectionFlag(raw, SettingsSectionFlags, out var ss))
            {
                openSettings = true;
                settingsSection = ss;
            }
            else if (TryMatchSectionFlag(raw, InfoSectionFlags, out var isec))
            {
                openInfo = true;
                infoSection = isec;
            }
        }

        return new LaunchParseResult
        {
            OpenSettings = openSettings,
            OpenInfo = openInfo,
            SettingsSection = settingsSection,
            InfoSection = infoSection,
        };
    }

    private static bool TryMatchSectionFlag(
        string arg,
        (string Long, string Short, string Section)[] table,
        out string section)
    {
        foreach (var (longForm, shortForm, sec) in table)
        {
            if (Matches(arg, longForm, shortForm))
            {
                section = sec;
                return true;
            }
        }

        section = null!;
        return false;
    }

    private static bool Matches(string arg, string longForm, string shortForm) =>
        string.Equals(arg, longForm, StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, shortForm, StringComparison.OrdinalIgnoreCase);
}
