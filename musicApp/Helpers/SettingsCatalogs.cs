using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace musicApp.Helpers;

public readonly record struct LanguageOption(string DisplayName, string Tag);

public static class AppLanguageCatalog
{
    public const string SystemLanguageTag = "en-system";

    public static IReadOnlyList<LanguageOption> AllLocales { get; } =
    [
        new LanguageOption("Arabic (Saudi Arabia)", "ar-SA"),
        new LanguageOption("Chinese (Simplified, China)", "zh-CN"),
        new LanguageOption("Chinese (Traditional, Taiwan)", "zh-TW"),
        new LanguageOption("Czech (Czechia)", "cs-CZ"),
        new LanguageOption("Danish (Denmark)", "da-DK"),
        new LanguageOption("Dutch (Netherlands)", "nl-NL"),
        new LanguageOption("English (United Kingdom)", "en-GB"),
        new LanguageOption("English (United States)", "en-US"),
        new LanguageOption("Finnish (Finland)", "fi-FI"),
        new LanguageOption("French (France)", "fr-FR"),
        new LanguageOption("German (Germany)", "de-DE"),
        new LanguageOption("Greek (Greece)", "el-GR"),
        new LanguageOption("Hebrew (Israel)", "he-IL"),
        new LanguageOption("Hindi (India)", "hi-IN"),
        new LanguageOption("Hungarian (Hungary)", "hu-HU"),
        new LanguageOption("Indonesian (Indonesia)", "id-ID"),
        new LanguageOption("Italian (Italy)", "it-IT"),
        new LanguageOption("Japanese (Japan)", "ja-JP"),
        new LanguageOption("Korean (South Korea)", "ko-KR"),
        new LanguageOption("Norwegian Bokmål (Norway)", "nb-NO"),
        new LanguageOption("Polish (Poland)", "pl-PL"),
        new LanguageOption("Portuguese (Brazil)", "pt-BR"),
        new LanguageOption("Portuguese (Portugal)", "pt-PT"),
        new LanguageOption("Romanian (Romania)", "ro-RO"),
        new LanguageOption("Russian (Russia)", "ru-RU"),
        new LanguageOption("Spanish (Mexico)", "es-MX"),
        new LanguageOption("Spanish (Spain)", "es-ES"),
        new LanguageOption("Swedish (Sweden)", "sv-SE"),
        new LanguageOption("Thai (Thailand)", "th-TH"),
        new LanguageOption("Turkish (Turkey)", "tr-TR"),
        new LanguageOption("Ukrainian (Ukraine)", "uk-UA"),
        new LanguageOption("Vietnamese (Vietnam)", "vi-VN"),
    ];

    public static void PopulateGeneralLanguageComboBox(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Items.Clear();

        combo.Items.Add(new ComboBoxItem { Content = "English (System)", Tag = SystemLanguageTag });

        var install = CultureInfo.InstalledUICulture;
        var installLang = install.TwoLetterISOLanguageName;

        var family = AllLocales
            .Where(o => string.Equals(GetTwoLetter(o.Tag), installLang, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var familyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in family)
            familyTags.Add(o.Tag);

        IEnumerable<LanguageOption> orderedFamily = family
            .OrderBy(o => string.Equals(o.Tag, install.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var opt in orderedFamily)
        {
            combo.Items.Add(new ComboBoxItem { Content = opt.DisplayName, Tag = opt.Tag });
        }

        if (family.Count > 0)
            combo.Items.Add(CreateDividerItem(combo));

        var rest = AllLocales
            .Where(o => !familyTags.Contains(o.Tag))
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var opt in rest)
            combo.Items.Add(new ComboBoxItem { Content = opt.DisplayName, Tag = opt.Tag });
    }

    private static string? GetTwoLetter(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static ComboBoxItem CreateDividerItem(ComboBox combo)
    {
        var brush = combo.TryFindResource("BorderDefault-brush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x52));

        return new ComboBoxItem
        {
            IsEnabled = false,
            Focusable = false,
            Content = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 4),
                Background = brush,
            },
        };
    }
}

public static class SystemFontCatalog
{
    /// <summary>Stored in preferences and on the first combo item for the OS message / UI font.</summary>
    public const string SystemDefaultTag = "";

    public static void PopulateFontFamilyComboBox(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Items.Clear();

        combo.Items.Add(new ComboBoxItem { Content = "System default", Tag = SystemDefaultTag });

        var ietf = CultureInfo.CurrentUICulture.IetfLanguageTag;
        if (string.IsNullOrEmpty(ietf))
            ietf = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(ietf))
            ietf = "en-US";
        var lang = XmlLanguage.GetLanguage(ietf);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Display, string Source)>();

        foreach (var ff in Fonts.SystemFontFamilies)
        {
            var source = ff.Source;
            if (string.IsNullOrEmpty(source) || !seen.Add(source))
                continue;

            string display;
            if (ff.FamilyNames.TryGetValue(lang, out var localized))
                display = localized;
            else if (ff.FamilyNames.Count > 0)
                display = ff.FamilyNames.Values.First();
            else
                display = source;

            entries.Add((display, source));
        }

        foreach (var (display, source) in entries.OrderBy(e => e.Display, StringComparer.CurrentCultureIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = display, Tag = source });
    }

    public static FontFamily ResolveFontFamily(string? tagOrSource)
    {
        if (string.IsNullOrEmpty(tagOrSource))
            return SystemFonts.MessageFontFamily;

        try
        {
            return new FontFamily(tagOrSource);
        }
        catch
        {
            return SystemFonts.MessageFontFamily;
        }
    }
}
