using ReadNest.Application.Abstractions;
using ReadNest.Application.Reading;
using ReadNest.Domain.Enums;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.UnitTests.Application;

public sealed class ReaderApplicationTests
{
    [Fact]
    public void Valid_pdf_and_epub_are_recognised_by_content()
    {
        Assert.Equal(ReadingFileFormat.Pdf, ReadingFileInspector.Inspect(new MemoryStream(TestFiles.Pdf())));
        Assert.Equal(ReadingFileFormat.Epub, ReadingFileInspector.Inspect(new MemoryStream(TestFiles.Epub())));
    }

    [Fact]
    public void Truncated_pdf_is_rejected()
    {
        var pdf = TestFiles.Pdf();
        var truncated = pdf[..(pdf.Length - 40)];

        var error = Assert.Throws<InvalidDataException>(() => ReadingFileInspector.Inspect(new MemoryStream(truncated)));
        Assert.Contains("incomplete", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NotEpubs))]
    public void Archives_that_are_not_epubs_are_rejected(byte[] content)
    {
        Assert.Throws<InvalidDataException>(() => ReadingFileInspector.Inspect(new MemoryStream(content)));
    }

    public static TheoryData<byte[]> NotEpubs() => new()
    {
        TestFiles.PlainZip(),
        TestFiles.Epub(mimetype: "application/zip"),
        TestFiles.Epub(includeContainer: false),
        new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 },
    };

    [Theory]
    [MemberData(nameof(OtherFiles))]
    public void Other_formats_are_rejected(byte[] content)
    {
        var error = Assert.Throws<InvalidDataException>(() => ReadingFileInspector.Inspect(new MemoryStream(content)));
        Assert.Contains("PDF or EPUB", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<byte[]> OtherFiles() => new()
    {
        TestFiles.Svg(),
        TestFiles.Image(20, 20),
        "plain text"u8.ToArray(),
        Array.Empty<byte>(),
    };

    [Theory]
    [InlineData("book.pdf", ReadingFileFormat.Pdf)]
    [InlineData("Book.EPUB", ReadingFileFormat.Epub)]
    [InlineData("book.txt", null)]
    [InlineData("book.pdf.exe", null)]
    [InlineData(null, null)]
    public void Format_from_name_only_accepts_pdf_and_epub(string? name, ReadingFileFormat? expected)
    {
        Assert.Equal(expected, ReadingFileInspector.FormatFromName(name));
    }

    [Fact]
    public void Display_names_drop_paths_and_control_characters()
    {
        Assert.Equal("book.pdf", ReadingFileInspector.DisplayName("C:\\Users\\me\\..\\book.pdf", ReadingFileFormat.Pdf));
        Assert.Equal("evil.pdf", ReadingFileInspector.DisplayName("../../etc/evil.pdf", ReadingFileFormat.Pdf));
        Assert.Equal("ab.epub", ReadingFileInspector.DisplayName("a\u0000b.epub", ReadingFileFormat.Epub));
        Assert.Equal("book.epub", ReadingFileInspector.DisplayName("   ", ReadingFileFormat.Epub));
    }

    [Theory]
    // A PDF with the same page count as the entry maps page for page.
    [InlineData(ReadingFileFormat.Pdf, 12, 300, null, 300, 12)]
    // Otherwise the percentage through the file is applied to the entry's page count.
    [InlineData(ReadingFileFormat.Pdf, 5, 10, 50.0, 200, 100)]
    [InlineData(ReadingFileFormat.Epub, null, null, 33.3, 300, 99)]
    // Without an entry page count only PDFs can be mapped.
    [InlineData(ReadingFileFormat.Pdf, 8, 10, 80.0, null, 8)]
    [InlineData(ReadingFileFormat.Epub, null, null, 50.0, null, null)]
    public void Reader_positions_map_to_book_pages(ReadingFileFormat format, int? page, int? pageCount, double? percent, int? totalPages, int? expected)
    {
        var bookPage = ReaderProgress.ToBookPage(format, page, pageCount, percent is null ? null : (decimal)percent, totalPages);

        Assert.Equal(expected, bookPage);
    }

    [Theory]
    [InlineData(10, 20, 5, true)]
    [InlineData(10, 10, 30, false)] // not forward
    [InlineData(10, 30, 0, false)] // under a minute
    [InlineData(10, 300, 2, false)] // a jump (for example to the last chapter), not reading
    [InlineData(0, 14, 3, true)] // up to about three pages a minute, plus a little slack
    public void Automatic_sessions_require_plausible_forward_reading(int from, int to, int minutes, bool expected)
    {
        Assert.Equal(expected, ReaderProgress.IsPlausibleAutoSession(from, to, minutes));
    }

    [Fact]
    public void Google_full_view_and_previews_are_labelled_honestly()
    {
        var full = Assert.Single(ReadingSourcesService.GoogleOptions(new GoogleAccessInfo("abcdef12", "ALL_PAGES", true, true, null)));
        Assert.Equal(ReadingAccess.FullText, full.Access);
        Assert.NotNull(full.ReaderPath);

        var preview = Assert.Single(ReadingSourcesService.GoogleOptions(new GoogleAccessInfo("abcdef12", "PARTIAL", true, false, "https://play.google.com/books/reader?id=abcdef12")));
        Assert.Equal(ReadingAccess.Preview, preview.Access);
        Assert.Equal("Read Preview", preview.Label);

        var notEmbeddable = Assert.Single(ReadingSourcesService.GoogleOptions(new GoogleAccessInfo("abcdef12", "PARTIAL", false, false, null)));
        Assert.Null(notEmbeddable.ReaderPath);
        Assert.NotNull(notEmbeddable.ExternalUrl);

        Assert.Empty(ReadingSourcesService.GoogleOptions(new GoogleAccessInfo("abcdef12", "NO_PAGES", false, false, null)));
    }

    [Fact]
    public void Open_library_only_offers_links()
    {
        var read = Assert.Single(ReadingSourcesService.OpenLibraryOptions(new OpenLibraryAccess("OL1W", "T", "public")));
        Assert.Equal(ReadingAccess.FullText, read.Access);
        Assert.Null(read.ReaderPath);
        Assert.StartsWith("https://openlibrary.org/works/OL1W", read.ExternalUrl, StringComparison.Ordinal);

        var borrow = Assert.Single(ReadingSourcesService.OpenLibraryOptions(new OpenLibraryAccess("OL1W", "T", "borrowable")));
        Assert.Equal(ReadingAccess.Borrow, borrow.Access);
        Assert.NotNull(borrow.Notice);

        Assert.Empty(ReadingSourcesService.OpenLibraryOptions(new OpenLibraryAccess("OL1W", "T", "printdisabled")));
        Assert.Empty(ReadingSourcesService.OpenLibraryOptions(new OpenLibraryAccess("OL1W", "T", "no_ebook")));
    }

    [Fact]
    public void Gutenberg_books_are_only_importable_when_marked_public_domain()
    {
        var publicDomain = ReadingSourcesService.GutenbergOption(new GutenbergBook(1342, "Pride and Prejudice", ["Austen, Jane"], false, "https://www.gutenberg.org/ebooks/1342.epub3.images", ["en"]));
        Assert.Equal(1342, publicDomain.GutenbergId);
        Assert.Equal(ReadingAccess.FullText, publicDomain.Access);
        Assert.Contains("United States", publicDomain.Notice, StringComparison.Ordinal);

        var unknown = ReadingSourcesService.GutenbergOption(new GutenbergBook(5, "Book", [], null, "https://www.gutenberg.org/ebooks/5.epub3.images", []));
        Assert.Null(unknown.GutenbergId);
        Assert.Equal(ReadingAccess.External, unknown.Access);

        var copyrighted = ReadingSourcesService.GutenbergOption(new GutenbergBook(6, "Book", [], true, "https://www.gutenberg.org/ebooks/6.epub3.images", []));
        Assert.Null(copyrighted.GutenbergId);
    }

    [Fact]
    public void Gutenberg_match_requires_the_same_title_and_author()
    {
        GutenbergBook[] results =
        [
            new(100, "Pride and Prejudice and Zombies", ["Grahame-Smith, Seth"], true, null, ["en"]),
            new(1342, "Pride and Prejudice", ["Austen, Jane"], false, "https://www.gutenberg.org/ebooks/1342.epub3.images", ["en"]),
            new(9999, "Pride and Prejudice", ["Someone, Else"], false, "https://www.gutenberg.org/ebooks/9999.epub3.images", ["en"]),
        ];

        Assert.Equal(1342, ReadingSourcesService.BestGutenbergMatch(results, "Pride and Prejudice: Annotated", "Jane Austen")?.Id);
        Assert.Null(ReadingSourcesService.BestGutenbergMatch(results, "Emma", "Jane Austen"));
        Assert.Null(ReadingSourcesService.BestGutenbergMatch(results, "Pride and Prejudice", "Charles Dickens"));
    }

    [Fact]
    public void Upload_sizes_are_described_for_people()
    {
        Assert.Equal("5 MB", UploadOptions.Describe(5 * 1024 * 1024));
        Assert.Equal("50 MB", UploadOptions.Describe(50 * 1024 * 1024));
        Assert.Equal("1 KB", UploadOptions.Describe(10));
    }
}
