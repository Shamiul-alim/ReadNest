using System.Globalization;

namespace ReadNest.Web.Components.Shared;

/// <summary>Plain-language date and number formats shared by the pages.</summary>
public static class Formats
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"Oct 4", or "Oct 4, 2025" outside the reference year.</summary>
    public static string Short(DateOnly date, DateOnly today) =>
        date.Year == today.Year ? date.ToString("MMM d", Culture) : date.ToString("MMM d, yyyy", Culture);

    /// <summary>"Oct 4, 2026".</summary>
    public static string Long(DateOnly date) => date.ToString("MMM d, yyyy", Culture);

    public static string Long(DateTime utc) => utc.ToString("MMM d, yyyy", Culture);

    /// <summary>"Today", "Yesterday", "3 days ago" (within a week) or a short date.</summary>
    public static string Relative(DateOnly date, DateOnly today)
    {
        var days = today.DayNumber - date.DayNumber;
        return days switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => days + " days ago",
            _ => Short(date, today),
        };
    }

    /// <summary>Lower-case relative date for use mid-sentence ("last read yesterday").</summary>
    public static string RelativeLower(DateOnly date, DateOnly today)
    {
        var text = Relative(date, today);
        return text is "Today" or "Yesterday" ? text.ToLowerInvariant() : text;
    }

    /// <summary>"45 min", "1 h", "1 h 20 min".</summary>
    public static string Duration(int minutes) => minutes switch
    {
        < 60 => minutes + " min",
        _ when minutes % 60 == 0 => minutes / 60 + " h",
        _ => minutes / 60 + " h " + minutes % 60 + " min",
    };

    public static string Count(int value, string singular, string? plural = null) =>
        value.ToString("N0", Culture) + " " + (value == 1 ? singular : plural ?? singular + "s");

    public static string Number(int value) => value.ToString("N0", Culture);
}
