using System;
using System.Globalization;

namespace musicApp.Helpers;

/// <summary>
/// Maps a library "date added" (calendar date, local) into a non-overlapping section used by Recently Added.
/// Finer sections (Today) take precedence over coarser ones (This Week). Ordering follows scanning albums newest-first.
/// </summary>
internal static class RecentlyAddedTimeline
{
    public static string GetSectionTitle(DateTime addedDateLocal, DateTime nowLocal, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var d = addedDateLocal.Date;
        var today = nowLocal.Date;
        if (d > today)
            d = today;

        if (d == today)
            return "Today";

        var startOfWeek = StartOfWeekContaining(today, culture);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        if (d >= startOfWeek && d < today)
            return "This Week";

        if (d.Year == today.Year && d.Month == today.Month && d < startOfWeek)
            return "This Month";

        var threeMoCut = today.AddMonths(-3);
        if (d >= threeMoCut && d < startOfMonth)
            return "Last 3 Months";

        var sixMoCut = today.AddMonths(-6);
        if (d >= sixMoCut && d < threeMoCut)
            return "Last 6 Months";

        if (d.Year == today.Year && d < sixMoCut)
        {
            return string.Format(culture, "{0:Y}", d);
        }

        return d.Year.ToString(culture);
    }

    private static DateTime StartOfWeekContaining(DateTime dateInWeek, CultureInfo culture)
    {
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        var day = dateInWeek.DayOfWeek;
        int diff = (7 + (day - first)) % 7;
        return dateInWeek.AddDays(-diff).Date;
    }
}
