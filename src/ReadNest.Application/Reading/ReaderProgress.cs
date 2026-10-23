using ReadNest.Domain.Enums;

namespace ReadNest.Application.Reading;

/// <summary>
/// Translates a position in a reading file into a page of the library entry, so in-browser reading feeds the
/// same progress and session tracking as manual updates.
/// </summary>
public static class ReaderProgress
{
    /// <summary>Shortest active reading time that is recorded as a session automatically.</summary>
    public const int MinAutoSessionMinutes = 1;

    /// <summary>
    /// The book page matching the reader position, or null when it cannot be worked out (an EPUB whose entry has
    /// no page count). A PDF with the same number of pages as the entry maps page for page; otherwise the
    /// percentage through the file is applied to the entry's page count.
    /// </summary>
    public static int? ToBookPage(ReadingFileFormat format, int? filePage, int? filePageCount, decimal? percent, int? totalPages)
    {
        if (totalPages is { } total and > 0)
        {
            if (format == ReadingFileFormat.Pdf && filePage is { } page && filePageCount == total)
            {
                return Math.Clamp(page, 0, total);
            }

            var fraction = percent is { } p
                ? p / 100m
                : format == ReadingFileFormat.Pdf && filePage is { } pdfPage && filePageCount is { } count and > 0
                    ? (decimal)pdfPage / count
                    : (decimal?)null;
            return fraction is { } f ? (int)Math.Floor(Math.Clamp(f, 0m, 1m) * total) : null;
        }

        // Without a page count on the entry, a PDF's own page numbers are the best measure available.
        return format == ReadingFileFormat.Pdf ? filePage : null;
    }

    /// <summary>
    /// Whether an automatic checkpoint looks like real reading rather than a jump (opening the table of contents
    /// and skipping to the end, for example). Implausible jumps are kept as the file bookmark only; the reader can
    /// still record them explicitly.
    /// </summary>
    public static bool IsPlausibleAutoSession(int fromPage, int toPage, int activeMinutes) =>
        toPage > fromPage
        && activeMinutes >= MinAutoSessionMinutes
        && toPage - fromPage <= 5 + (activeMinutes * 3);
}
