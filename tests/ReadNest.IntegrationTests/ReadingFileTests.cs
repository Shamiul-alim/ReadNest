using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Library;
using ReadNest.Application.Reading;
using ReadNest.IntegrationTests.Infrastructure;
using static ReadNest.IntegrationTests.Infrastructure.FormExtensions;

namespace ReadNest.IntegrationTests;

/// <summary>Private PDF/EPUB files on library entries: validation, ownership, positions and progress.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReadingFileTests(ReadNestFactory factory)
{
    private static MultipartFormDataContent Upload(byte[] content, string name, string type = "application/octet-stream")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private static async Task<Guid> AddBookAsync(TestUser user, string? title = null, int? pages = 120)
    {
        var response = await user.Client.PostJsonAsync("/api/v1/library/manual", new { title = title ?? "Book " + Guid.NewGuid().ToString("N")[..8], authors = new[] { "Test Author" }, pageCount = pages });
        return JsonDocument.Parse(await response.ReadSuccessStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> UploadAsync(TestUser user, Guid entryId, byte[] content, string name)
    {
        var response = await user.Client.PostAsync($"/api/v1/library/{entryId}/reading-file", Upload(content, name));
        return JsonDocument.Parse(await response.ReadSuccessStringAsync()).RootElement;
    }

    private string[] StoredBooks() =>
        Directory.Exists(Path.Combine(factory.StorageRoot, "books"))
            ? Directory.GetFiles(Path.Combine(factory.StorageRoot, "books"), "*", SearchOption.AllDirectories)
            : [];

    [Fact]
    public async Task Pdf_upload_is_stored_privately_and_streamed_back_with_ranges()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);
        var pdf = TestFiles.Pdf(5);

        var file = await UploadAsync(user, entryId, pdf, "My Book.pdf");

        Assert.Equal("Pdf", file.GetProperty("format").GetString());
        Assert.Equal("My Book.pdf", file.GetProperty("fileName").GetString());
        Assert.Equal(pdf.Length, file.GetProperty("sizeBytes").GetInt64());

        var content = await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("application/pdf", content.Content.Headers.ContentType!.MediaType);
        Assert.Equal(pdf, await content.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", content.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains("sandbox", content.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Null(content.Content.Headers.ContentDisposition);

        var download = await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file/content?download=true");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/library/{entryId}/reading-file/content");
        request.Headers.Range = new RangeHeaderValue(0, 7);
        var partial = await user.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("%PDF-1.4"u8.ToArray(), await partial.Content.ReadAsByteArrayAsync());

        // Stored outside wwwroot under a random name, never the uploaded name.
        Assert.DoesNotContain(StoredBooks(), f => f.Contains("My Book", StringComparison.Ordinal));
        Assert.DoesNotContain("wwwroot", factory.StorageRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Epub_upload_is_accepted()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);

        var file = await UploadAsync(user, entryId, TestFiles.Epub(withScript: true), "novel.epub");

        Assert.Equal("Epub", file.GetProperty("format").GetString());
        var content = await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file/content");
        Assert.Equal("application/epub+zip", content.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_files_are_rejected_without_being_stored(byte[] content, string name)
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);
        var before = StoredBooks().Length;

        var response = await user.Client.PostAsync($"/api/v1/library/{entryId}/reading-file", Upload(content, name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, StoredBooks().Length);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file")).StatusCode);
    }

    public static TheoryData<byte[], string> Invalid() => new()
    {
        { TestFiles.Pdf()[..200], "cut.pdf" },
        { TestFiles.Epub(), "mislabelled.pdf" },
        { TestFiles.Pdf(), "mislabelled.epub" },
        { TestFiles.PlainZip(), "archive.epub" },
        { TestFiles.Epub(mimetype: "application/zip"), "fake.epub" },
        { TestFiles.Svg(), "image.pdf" },
        { TestFiles.Pdf(), "book.txt" },
        { new byte[ReadNestFactory.UploadLimit + 1], "huge.pdf" },
    };

    [Fact]
    public async Task Other_users_cannot_read_change_or_find_a_private_file()
    {
        var owner = await factory.CreateUserAsync();
        var stranger = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(owner);
        await UploadAsync(owner, entryId, TestFiles.Pdf(), "private.pdf");
        var baseUrl = $"/api/v1/library/{entryId}/reading-file";

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(baseUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(baseUrl + "/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PutJsonAsync(baseUrl + "/position", new { location = "1", percent = 1, page = 1, pageCount = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsync(baseUrl, Upload(TestFiles.Pdf(), "replace.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.DeleteAsync(baseUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(baseUrl + "/content")).StatusCode);

        // The owner's file is untouched.
        var content = await owner.Client.GetAsync(baseUrl + "/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
    }

    [Fact]
    public async Task Reader_page_requires_sign_in()
    {
        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync($"/reader/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/Login", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Positions_are_saved_and_validated_without_recording_sessions()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);
        await UploadAsync(user, entryId, TestFiles.Pdf(10), "book.pdf");
        var url = $"/api/v1/library/{entryId}/reading-file/position";

        var saved = JsonDocument.Parse(await (await user.Client.PutJsonAsync(url, new { location = "4", percent = 40m, page = 4, pageCount = 10 })).ReadSuccessStringAsync()).RootElement;
        Assert.Equal(4, saved.GetProperty("page").GetInt32());

        for (var page = 5; page <= 8; page++)
        {
            (await user.Client.PutJsonAsync(url, new { location = page.ToString(System.Globalization.CultureInfo.InvariantCulture), percent = page * 10m, page, pageCount = 10 })).EnsureSuccessStatusCode();
        }

        var file = JsonDocument.Parse(await (await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file")).ReadSuccessStringAsync()).RootElement;
        Assert.Equal("8", file.GetProperty("position").GetProperty("location").GetString());
        Assert.Equal(80m, file.GetProperty("position").GetProperty("percent").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutJsonAsync(url, new { location = "4", percent = 140m, page = 4, pageCount = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutJsonAsync(url, new { location = "javascript:alert(1)", percent = 10m, page = 1, pageCount = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutJsonAsync(url, new { location = "11", percent = 10m, page = 11, pageCount = 10 })).StatusCode);

        // Saving a bookmark is not reading activity: no sessions, no page or status change.
        var sessions = JsonDocument.Parse(await (await user.Client.GetAsync($"/api/v1/sessions?entryId={entryId}")).ReadSuccessStringAsync()).RootElement;
        Assert.Equal(0, sessions.GetProperty("totalCount").GetInt32());
        var entry = await factory.WithServiceAsync<LibraryEntryDetailsDto, LibraryService>(s => s.GetEntryAsync(user.Id, entryId, CancellationToken.None));
        Assert.Equal(0, entry.CurrentPage);
        Assert.Equal(Domain.Enums.ReadingStatus.WantToRead, entry.Status);
    }

    [Fact]
    public async Task Reading_in_the_browser_records_progress_through_the_normal_session_workflow()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user, pages: 10);
        await UploadAsync(user, entryId, TestFiles.Pdf(10), "book.pdf");

        // What the reader does at a checkpoint: map the PDF page to a book page and apply it as progress with a session.
        var page = ReaderProgress.ToBookPage(Domain.Enums.ReadingFileFormat.Pdf, 6, 10, 60m, 10)!.Value;
        Assert.True(ReaderProgress.IsPlausibleAutoSession(0, page, 4));
        var result = await factory.WithServiceAsync<ProgressUpdateResult, LibraryService>(s =>
            s.ApplyProgressAsync(user.Id, entryId, new UpdateProgressRequest(page, RecordSession: true, DurationMinutes: 4), CancellationToken.None));

        Assert.Equal(6, result.Entry.CurrentPage);
        Assert.Equal(Domain.Enums.ReadingStatus.CurrentlyReading, result.Entry.Status);
        Assert.NotNull(result.RecordedSessionId);

        // Reaching the last page moves the bookmark but never marks the book as read.
        var end = await factory.WithServiceAsync<ProgressUpdateResult, LibraryService>(s =>
            s.ApplyProgressAsync(user.Id, entryId, new UpdateProgressRequest(10, RecordSession: true, DurationMinutes: 3), CancellationToken.None));
        Assert.Equal(10, end.Entry.CurrentPage);
        Assert.Equal(Domain.Enums.ReadingStatus.CurrentlyReading, end.Entry.Status);
        Assert.Null(end.Entry.CompletedOn);

        var sessions = JsonDocument.Parse(await (await user.Client.GetAsync($"/api/v1/sessions?entryId={entryId}")).ReadSuccessStringAsync()).RootElement;
        Assert.Equal(2, sessions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Replacing_keeps_progress_and_removes_the_old_file()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user, pages: 50);
        await UploadAsync(user, entryId, TestFiles.Pdf(), "first.pdf");
        await factory.WithServiceAsync<LibraryService>(s => s.UpdateProgressAsync(user.Id, entryId, new UpdateProgressRequest(20), CancellationToken.None));
        (await user.Client.PutJsonAsync($"/api/v1/library/{entryId}/reading-file/position", new { location = "2", percent = 50m, page = 2, pageCount = 3 })).EnsureSuccessStatusCode();
        var before = StoredBooks().Length;

        var replaced = await UploadAsync(user, entryId, TestFiles.Epub(), "second.epub");

        Assert.Equal("Epub", replaced.GetProperty("format").GetString());
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("position").GetProperty("location").ValueKind);
        Assert.Equal(before, StoredBooks().Length);
        var entry = await factory.WithServiceAsync<LibraryEntryDetailsDto, LibraryService>(s => s.GetEntryAsync(user.Id, entryId, CancellationToken.None));
        Assert.Equal(20, entry.CurrentPage);
        Assert.Equal(1, entry.SessionCount);
    }

    [Fact]
    public async Task Removing_the_file_or_the_entry_deletes_the_stored_file()
    {
        var user = await factory.CreateUserAsync();
        var first = await AddBookAsync(user);
        var second = await AddBookAsync(user);
        var before = StoredBooks().Length;
        await UploadAsync(user, first, TestFiles.Pdf(), "a.pdf");
        await UploadAsync(user, second, TestFiles.Pdf(), "b.pdf");
        Assert.Equal(before + 2, StoredBooks().Length);

        (await user.Client.DeleteAsync($"/api/v1/library/{first}/reading-file")).EnsureSuccessStatusCode();
        Assert.Equal(before + 1, StoredBooks().Length);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/v1/library/{first}/reading-file/content")).StatusCode);

        (await user.Client.DeleteAsync($"/api/v1/library/{second}")).EnsureSuccessStatusCode();
        Assert.Equal(before, StoredBooks().Length);
    }

    [Fact]
    public async Task A_file_missing_from_storage_is_reported_as_not_found()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);
        var before = StoredBooks().ToHashSet();
        await UploadAsync(user, entryId, TestFiles.Pdf(), "gone.pdf");
        var stored = StoredBooks().Single(f => !before.Contains(f));

        File.Delete(stored);
        var response = await user.Client.GetAsync($"/api/v1/library/{entryId}/reading-file/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("missing", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_domain_gutenberg_books_can_be_attached_with_attribution()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user, "Gutenberg Test " + Guid.NewGuid().ToString("N")[..6]);
        factory.ReadingSources.Gutenberg.Add(new GutenbergBook(91001, "Free Book", ["Author, Old"], false, "https://www.gutenberg.org/ebooks/91001.epub3.images", ["en"]));
        factory.ReadingSources.Gutenberg.Add(new GutenbergBook(91002, "Protected Book", ["Author, New"], true, "https://www.gutenberg.org/ebooks/91002.epub3.images", ["en"]));
        var downloads = factory.ReadingSources.Downloads;

        var imported = JsonDocument.Parse(await (await user.Client.PostJsonAsync($"/api/v1/library/{entryId}/reading-file/gutenberg", new { gutenbergId = 91001 })).ReadSuccessStringAsync()).RootElement;
        Assert.Equal("ProjectGutenberg", imported.GetProperty("source").GetString());
        Assert.Equal("https://www.gutenberg.org/ebooks/91001", imported.GetProperty("sourceUrl").GetString());
        Assert.Contains("public domain in the United States", imported.GetProperty("sourceNote").GetString(), StringComparison.Ordinal);
        Assert.Equal(downloads + 1, factory.ReadingSources.Downloads);

        var other = await AddBookAsync(user);
        var refused = await user.Client.PostJsonAsync($"/api/v1/library/{other}/reading-file/gutenberg", new { gutenbergId = 91002 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var stranger = await factory.CreateUserAsync();
        var notOwned = await stranger.Client.PostJsonAsync($"/api/v1/library/{entryId}/reading-file/gutenberg", new { gutenbergId = 91001 });
        Assert.Equal(HttpStatusCode.NotFound, notOwned.StatusCode);
        Assert.Equal(downloads + 1, factory.ReadingSources.Downloads);
    }

    [Fact]
    public async Task Reading_sources_only_report_what_providers_offer()
    {
        var user = await factory.CreateUserAsync();
        var added = await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "refactoring" });
        var bookId = JsonDocument.Parse(await added.ReadSuccessStringAsync()).RootElement.GetProperty("bookId").GetGuid();
        factory.ReadingSources.Google = new GoogleAccessInfo("refactoring", "PARTIAL", true, false, null);
        factory.ReadingSources.OpenLibrary = new OpenLibraryAccess("OL123W", "Refactoring", "borrowable");

        var sources = JsonDocument.Parse(await (await user.Client.GetAsync($"/api/v1/books/{bookId}/reading-sources")).ReadSuccessStringAsync()).RootElement;

        var google = sources.EnumerateArray().Single(s => s.GetProperty("provider").GetString() == "GoogleBooks");
        var preview = Assert.Single(google.GetProperty("options").EnumerateArray());
        Assert.Equal("Preview", preview.GetProperty("access").GetString());
        Assert.Equal("Read Preview", preview.GetProperty("label").GetString());

        var openLibrary = sources.EnumerateArray().Single(s => s.GetProperty("provider").GetString() == "OpenLibrary");
        var borrow = Assert.Single(openLibrary.GetProperty("options").EnumerateArray());
        Assert.Equal("Borrow", borrow.GetProperty("access").GetString());
        Assert.Equal(JsonValueKind.Null, borrow.GetProperty("readerPath").ValueKind);

        var own = sources.EnumerateArray().Single(s => s.GetProperty("provider").GetString() == "YourFile");
        Assert.Empty(own.GetProperty("options").EnumerateArray());
        factory.ReadingSources.Google = null;
        factory.ReadingSources.OpenLibrary = null;
    }

    [Fact]
    public async Task Deleting_the_account_deletes_its_uploaded_files()
    {
        var user = await factory.CreateUserAsync();
        var entryId = await AddBookAsync(user);
        var before = StoredBooks().ToHashSet();
        await UploadAsync(user, entryId, TestFiles.Pdf(), "mine.pdf");
        var mine = StoredBooks().Single(f => !before.Contains(f));
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await browser.GetStringAsync("/Account/Login");
        (await browser.PostAsync("/Account/Login", Form(("_handler", "login"), ("__RequestVerificationToken", AntiforgeryToken(login)), ("Input.Email", user.Email), ("Input.Password", ReadNestFactory.Password)))).EnsureSuccessOrRedirect();

        var page = await browser.GetStringAsync("/Account/Manage/DeletePersonalData");
        var deleted = await browser.PostAsync("/Account/Manage/DeletePersonalData", Form(("_handler", "delete-user"), ("__RequestVerificationToken", AntiforgeryToken(page)), ("Input.Password", ReadNestFactory.Password)));

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.False(File.Exists(mine));
    }
}
