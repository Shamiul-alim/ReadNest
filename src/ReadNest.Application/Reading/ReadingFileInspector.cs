using System.IO.Compression;
using System.Text;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Reading;

/// <summary>
/// Identifies uploaded reading files from their contents. The name and declared content type are only used to
/// reject mismatches; the format always comes from the bytes. Nothing is extracted to disk: an EPUB's archive
/// directory is read in place.
/// </summary>
public static class ReadingFileInspector
{
    /// <summary>Most entries an EPUB may contain.</summary>
    public const int MaxEpubEntries = 10_000;

    /// <summary>Largest total uncompressed size an EPUB may declare (protects readers from zip bombs).</summary>
    public const long MaxEpubUncompressedBytes = 500L * 1024 * 1024;

    /// <summary>Largest single uncompressed entry an EPUB may declare.</summary>
    public const long MaxEpubEntryBytes = 150L * 1024 * 1024;

    private const string EpubMimeType = "application/epub+zip";

    /// <summary>The format suggested by a file name, or null when the extension is not .pdf or .epub.</summary>
    public static ReadingFileFormat? FormatFromName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => ReadingFileFormat.Pdf,
            ".epub" => ReadingFileFormat.Epub,
            _ => null,
        };

    public static string ExtensionFor(ReadingFileFormat format) => format == ReadingFileFormat.Pdf ? "pdf" : "epub";

    public static string ContentTypeFor(ReadingFileFormat format) => format == ReadingFileFormat.Pdf ? "application/pdf" : EpubMimeType;

    /// <summary>
    /// Checks that <paramref name="content"/> is a well-formed PDF or EPUB and returns its format.
    /// Throws <see cref="InvalidDataException"/> with a user-safe message otherwise.
    /// </summary>
    public static ReadingFileFormat Inspect(Stream content)
    {
        Span<byte> head = stackalloc byte[8];
        content.Position = 0;
        var read = content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];

        if (head.StartsWith("%PDF-"u8))
        {
            InspectPdf(content);
            return ReadingFileFormat.Pdf;
        }

        if (head.StartsWith("PK\u0003\u0004"u8))
        {
            InspectEpub(content);
            return ReadingFileFormat.Epub;
        }

        throw new InvalidDataException("This isn't a PDF or EPUB file. Only PDF and EPUB books can be read in ReadNest.");
    }

    private static void InspectPdf(Stream content)
    {
        // A complete PDF ends with an %%EOF marker (allowing for trailing whitespace or a small tail).
        const int tail = 2048;
        var length = content.Length;
        var size = (int)Math.Min(tail, length);
        var buffer = new byte[size];
        content.Position = length - size;
        content.ReadExactly(buffer);
        if (buffer.AsSpan().IndexOf("%%EOF"u8) < 0)
        {
            throw new InvalidDataException("This PDF looks incomplete or damaged. Try exporting it again.");
        }
    }

    private static void InspectEpub(Stream content)
    {
        content.Position = 0;
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zip.Entries;
            if (entries.Count > MaxEpubEntries)
            {
                throw new InvalidDataException("This EPUB contains too many files.");
            }

            long total = 0;
            foreach (var entry in entries)
            {
                if (entry.Length > MaxEpubEntryBytes)
                {
                    throw new InvalidDataException("This EPUB contains a file that is too large.");
                }

                total += entry.Length;
                if (total > MaxEpubUncompressedBytes)
                {
                    throw new InvalidDataException("This EPUB expands to more than 500 MB, which is too large to read.");
                }
            }

            var mimetype = zip.GetEntry("mimetype");
            if (mimetype is null || mimetype.Length > 100 || ReadSmallText(mimetype).Trim() != EpubMimeType)
            {
                throw new InvalidDataException("This ZIP file isn't an EPUB book.");
            }

            if (zip.GetEntry("META-INF/container.xml") is null)
            {
                throw new InvalidDataException("This EPUB is missing its table of contents (META-INF/container.xml).");
            }
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("This ", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            throw new InvalidDataException("This EPUB looks damaged and could not be opened.", ex);
        }
    }

    private static string ReadSmallText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var buffer = new byte[128];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    /// <summary>A display-only file name: no path, no control characters, at most 255 characters.</summary>
    public static string DisplayName(string? fileName, ReadingFileFormat format)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/')[^1]);
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "book." + ExtensionFor(format);
        }

        return cleaned.Length > 255 ? cleaned[..200] + "." + ExtensionFor(format) : cleaned;
    }
}
