using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReadNest.Application.Abstractions;
using ReadNest.Infrastructure.ExternalBooks;
using ReadNest.Infrastructure.Imaging;
using ReadNest.Infrastructure.Storage;
using ReadNest.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace ReadNest.UnitTests.Infrastructure;

public sealed class FileHandlingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "readnest-store-" + Guid.NewGuid().ToString("N"));
    private readonly SkiaImageProcessor _images = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LocalFileStore NewStore()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        return new LocalFileStore(Options.Create(new StorageOptions { RootPath = _root }), environment, TimeProvider.System, NullLogger<LocalFileStore>.Instance);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, 1200, 800)]
    [InlineData(SKEncodedImageFormat.Jpeg, 640, 900)]
    [InlineData(SKEncodedImageFormat.Webp, 300, 300)]
    public void Photos_are_cropped_square_resized_and_reencoded_as_webp(SKEncodedImageFormat format, int width, int height)
    {
        var output = _images.NormalizeProfilePhoto(TestFiles.Image(width, height, format), 512);

        Assert.Equal(SKEncodedImageFormat.Webp, SkiaImageProcessor.Sniff(output));
        using var codec = SKCodec.Create(new MemoryStream(output));
        var expected = Math.Min(512, Math.Min(width, height));
        Assert.Equal(expected, codec.Info.Width);
        Assert.Equal(expected, codec.Info.Height);
    }

    [Theory]
    [MemberData(nameof(BadImages))]
    public void Unsupported_or_damaged_images_are_rejected(byte[] content)
    {
        Assert.Throws<InvalidImageException>(() => _images.NormalizeProfilePhoto(content, 512));
    }

    public static TheoryData<byte[]> BadImages() => new()
    {
        TestFiles.Svg(),
        TestFiles.Image(40, 40, SKEncodedImageFormat.Png)[..60],
        "GIF89a\u0001\u0000\u0001\u0000"u8.ToArray(),
        TestFiles.Pdf(),
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 },
        Array.Empty<byte>(),
    };

    [Fact]
    public void Decompression_bombs_are_rejected_before_decoding()
    {
        var error = Assert.Throws<InvalidImageException>(() => _images.NormalizeProfilePhoto(TestFiles.PngBomb(), 512));

        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_whose_signature_and_content_disagree_is_rejected()
    {
        // A PNG body behind a JPEG signature.
        byte[] png = TestFiles.Image(30, 30);
        byte[] disguised = [0xFF, 0xD8, 0xFF, .. png[3..]];

        Assert.Throws<InvalidImageException>(() => _images.NormalizeProfilePhoto(disguised, 512));
    }

    [Fact]
    public async Task Staged_files_are_hashed_committed_and_deleted()
    {
        var store = NewStore();
        var content = TestFiles.Pdf();

        string key;
        await using (var staged = await store.StageAsync(new MemoryStream(content), 1024 * 1024, CancellationToken.None))
        {
            Assert.Equal(content.Length, staged.Length);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), staged.Sha256);
            key = await staged.CommitAsync("books", "pdf", CancellationToken.None);
        }

        Assert.Matches("^books/[0-9]{4}/[0-9a-f]{32}\\.pdf$", key);
        await using (var stream = store.OpenRead(key))
        {
            Assert.NotNull(stream);
            Assert.Equal(content.Length, stream.Length);
        }

        await store.DeleteAsync(key, CancellationToken.None);
        Assert.Null(store.OpenRead(key));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".staging")));
    }

    [Fact]
    public async Task Oversized_uploads_stop_early_and_leave_nothing_behind()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<FileTooLargeException>(() => store.StageAsync(new MemoryStream(new byte[5000]), 4096, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".staging")));
    }

    [Fact]
    public async Task Uncommitted_staged_files_are_discarded()
    {
        var store = NewStore();

        await using (await store.StageAsync(new MemoryStream(new byte[10]), 100, CancellationToken.None))
        {
            Assert.Single(Directory.GetFiles(Path.Combine(_root, ".staging")));
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".staging")));
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("books/2026/../../appsettings.json")]
    [InlineData("books/2026/ABC.pdf")]
    [InlineData("photos/2026/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    public void Keys_that_are_not_store_generated_are_never_opened(string key)
    {
        Assert.False(LocalFileStore.IsValidKey(key));
        Assert.Null(NewStore().OpenRead(key));
    }

    [Theory]
    [InlineData("https://www.gutenberg.org/ebooks/1342.epub3.images", true)]
    [InlineData("https://gutenberg.org/cache/epub/1342/pg1342-images-3.epub", true)]
    [InlineData("http://www.gutenberg.org/ebooks/1342.epub3.images", false)]
    [InlineData("https://www.gutenberg.org.evil.example/ebooks/1.epub", false)]
    [InlineData("https://www.gutenberg.org:8443/ebooks/1.epub", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Gutenberg_downloads_are_limited_to_gutenberg_org_over_https(string url, bool allowed)
    {
        Assert.Equal(allowed, ReadingSourceProvider.IsGutenbergUrl(url));
    }

    [Fact]
    public void Gutendex_records_without_a_safe_epub_link_are_not_importable()
    {
        var book = ReadingSourceProvider.Map(new ReadingSourceProvider.GutendexBook
        {
            Id = 1,
            Title = "Book",
            Copyright = false,
            MediaType = "Text",
            Formats = new() { ["application/epub+zip"] = "https://mirror.example/1.epub" },
        });

        Assert.NotNull(book);
        Assert.Null(book.EpubUrl);
        Assert.Null(ReadingSourceProvider.Map(new ReadingSourceProvider.GutendexBook { Id = 2, Title = "Audio", MediaType = "Sound" }));
    }

    [Fact]
    public void Google_viewability_fallback_is_parsed_from_the_official_response()
    {
        // Shape of https://books.google.com/books?jscmd=viewapi&bibkeys=...&callback=cb (captured 2026-10-23).
        const string body = """
            cb({"ISBN:0738531367":{"bib_key":"ISBN:0738531367","info_url":"https://books.google.com/books?id=VpNa9UckT24C&source=gbs_ViewAPI","preview_url":"https://books.google.com/books?id=VpNa9UckT24C&printsec=frontcover&source=gbs_ViewAPI","preview":"partial","embeddable":true},"s1gVAAAAYAAJ":{"bib_key":"s1gVAAAAYAAJ","preview_url":"https://books.google.com/books?id=s1gVAAAAYAAJ&source=gbs_ViewAPI","preview":"noview","embeddable":false}});
            """;

        var partial = ReadingSourceProvider.ParseViewability(body, "ISBN:0738531367");
        Assert.NotNull(partial);
        Assert.Equal("VpNa9UckT24C", partial.VolumeId);
        Assert.Equal("PARTIAL", partial.Viewability);
        Assert.True(partial.Embeddable);

        var none = ReadingSourceProvider.ParseViewability(body, "s1gVAAAAYAAJ");
        Assert.Equal("NO_PAGES", none!.Viewability);

        Assert.Null(ReadingSourceProvider.ParseViewability(body, "ISBN:missing"));
        Assert.Null(ReadingSourceProvider.ParseViewability("not json", "x"));
        Assert.Null(ReadingSourceProvider.ParseViewability("cb({\"k\":{\"preview_url\":\"https://evil.example/books?id=abcdefgh\",\"preview\":\"full\"}})", "k"));
    }
}
