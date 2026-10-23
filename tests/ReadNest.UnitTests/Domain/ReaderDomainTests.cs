using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.UnitTests.Domain;

public sealed class ReaderDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    private static ReadingFile NewFile(ReadingFileFormat format = ReadingFileFormat.Pdf)
    {
        var entry = UserBook.Create(Guid.NewGuid(), UserBookTests.NewBook(), ReadingStatus.CurrentlyReading, Today, Now);
        return ReadingFile.Create(entry, format, ReadingFileSource.Upload, "books/2026/" + new string('0', 32) + ".pdf", "book.pdf", 1234, Hash, null, null, Now);
    }

    [Fact]
    public void Pdf_position_keeps_page_and_page_count()
    {
        var file = NewFile();

        file.UpdatePosition("7", 25m, 7, 28, Now);

        Assert.Equal("7", file.LastLocation);
        Assert.Equal(25m, file.LastPercent);
        Assert.Equal(7, file.LastPage);
        Assert.Equal(28, file.PageCount);
        Assert.Equal(Now, file.PositionUpdatedAtUtc);
    }

    [Fact]
    public void Epub_position_stores_the_cfi_and_ignores_pages()
    {
        var file = NewFile(ReadingFileFormat.Epub);

        file.UpdatePosition("epubcfi(/6/4!/4/2/1:0)", 12.345m, 3, 10, Now);

        Assert.Equal("epubcfi(/6/4!/4/2/1:0)", file.LastLocation);
        Assert.Equal(12.35m, file.LastPercent);
        Assert.Null(file.LastPage);
        Assert.Null(file.PageCount);
    }

    [Theory]
    [InlineData("3", 101, 3, 10)]
    [InlineData("3", -1, 3, 10)]
    [InlineData("11", 50, 11, 10)]
    [InlineData("0", 0, 0, 10)]
    [InlineData("chapter-2", 10, 2, 10)]
    public void Invalid_positions_are_rejected(string location, int percent, int page, int pageCount)
    {
        var file = NewFile();

        Assert.Throws<DomainException>(() => file.UpdatePosition(location, percent, page, pageCount, Now));
    }

    [Fact]
    public void Replacing_the_file_returns_the_old_key_and_clears_the_position()
    {
        var file = NewFile();
        var oldKey = file.StorageKey;
        file.UpdatePosition("4", 40m, 4, 10, Now);

        var replaced = file.Replace(ReadingFileFormat.Epub, ReadingFileSource.Upload, "books/2026/" + new string('1', 32) + ".epub", "new.epub", 99, Hash, null, null, Now);

        Assert.Equal(oldKey, replaced);
        Assert.Equal(ReadingFileFormat.Epub, file.Format);
        Assert.Null(file.LastLocation);
        Assert.Null(file.LastPercent);
        Assert.Null(file.LastPage);
    }

    [Fact]
    public void Empty_files_and_bad_checksums_are_rejected()
    {
        var entry = UserBook.Create(Guid.NewGuid(), UserBookTests.NewBook(), ReadingStatus.CurrentlyReading, Today, Now);

        Assert.Throws<DomainException>(() => ReadingFile.Create(entry, ReadingFileFormat.Pdf, ReadingFileSource.Upload, "k", "a.pdf", 0, Hash, null, null, Now));
        Assert.Throws<DomainException>(() => ReadingFile.Create(entry, ReadingFileFormat.Pdf, ReadingFileSource.Upload, "k", "a.pdf", 10, "abc", null, null, Now));
    }

    [Fact]
    public void Profile_photo_replaces_a_link_and_returns_the_previous_key()
    {
        var profile = UserProfile.Create(Guid.NewGuid(), "reader", "Reader", "UTC", Now);
        profile.UpdateProfile("Reader", "reader", null, "https://example.com/me.png", Now);

        Assert.Null(profile.SetPhoto("photos/2026/a.webp", Now));
        Assert.Null(profile.ProfileImageUrl);
        Assert.Equal("photos/2026/a.webp", profile.SetPhoto("photos/2026/b.webp", Now.AddMinutes(1)));
        Assert.Equal("photos/2026/b.webp", profile.RemovePhoto(Now.AddMinutes(2)));
        Assert.Null(profile.PhotoKey);
    }

    [Fact]
    public void Photo_url_points_at_the_photo_endpoint_only_for_uploaded_photos()
    {
        var id = Guid.Parse("01a165c0-4c57-70ed-b424-99368498afa6");

        Assert.Equal("api/v1/users/01a165c04c5770edb42499368498afa6/photo?v=42", UserProfile.PhotoUrlFor(id, "photos/x.webp", 42, null));
        Assert.Equal("https://example.com/me.png", UserProfile.PhotoUrlFor(id, null, 0, "https://example.com/me.png"));
        Assert.Null(UserProfile.PhotoUrlFor(id, null, 0, null));
    }

    [Fact]
    public void Reader_preferences_are_range_checked()
    {
        var profile = UserProfile.Create(Guid.NewGuid(), "reader", "Reader", "UTC", Now);

        profile.UpdateReaderPreferences(ReaderTheme.Night, 140, 180, ReaderWidth.Wide, ReaderFlow.Scrolled, Now);
        Assert.Equal(ReaderTheme.Night, profile.ReaderTheme);
        Assert.Equal(140, profile.ReaderFontScale);

        Assert.Throws<DomainException>(() => profile.UpdateReaderPreferences(ReaderTheme.Paper, 300, 160, ReaderWidth.Medium, ReaderFlow.Paginated, Now));
        Assert.Throws<DomainException>(() => profile.UpdateReaderPreferences(ReaderTheme.Paper, 100, 50, ReaderWidth.Medium, ReaderFlow.Paginated, Now));
        Assert.Throws<DomainException>(() => profile.UpdateReaderPreferences((ReaderTheme)99, 100, 160, ReaderWidth.Medium, ReaderFlow.Paginated, Now));
    }
}
