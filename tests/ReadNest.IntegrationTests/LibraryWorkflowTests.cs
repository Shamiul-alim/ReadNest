using System.Net;
using System.Text.Json;
using ReadNest.Application.Analytics;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Application.Notes;
using ReadNest.Application.Reading;
using ReadNest.Application.Reviews;
using ReadNest.Domain.Enums;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class LibraryWorkflowTests(ReadNestFactory factory)
{
    private static async Task<LibraryEntryDetailsDto> AddAsync(HttpClient client, CatalogBook book, ReadingStatus status = ReadingStatus.WantToRead)
    {
        var response = await client.PostJsonAsync("/api/v1/library", new { provider = book.Provider, externalId = book.ExternalId, status });
        return await response.ReadAsync<LibraryEntryDetailsDto>();
    }

    [Fact]
    public async Task Search_add_track_and_finish_a_book()
    {
        var user = await factory.CreateUserAsync();
        var client = user.Client;

        var search = await (await client.GetAsync("/api/v1/catalog/search?q=clean&field=Title")).ReadAsync<CatalogSearchResult>();
        Assert.Contains(search.Items, b => b.ExternalId == "clean-code");

        var add = await client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "CurrentlyReading" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var entry = await add.ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal("Clean Code", entry.Title);
        Assert.Equal(464, entry.TotalPages);
        Assert.Equal(ReadingStatus.CurrentlyReading, entry.Status);

        // Adding the same book again returns the existing entry instead of a duplicate.
        var again = await client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(entry.Id, (await again.ReadAsync<LibraryEntryDetailsDto>()).Id);

        var progress = await (await client.PutJsonAsync($"/api/v1/library/{entry.Id}/progress", new { currentPage = 180, recordSession = true, durationMinutes = 45 }))
            .ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(180, progress.CurrentPage);
        Assert.Equal(38.79m, progress.ProgressPercent);
        Assert.Equal(1, progress.SessionCount);

        var session = await (await client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 180, endPage = 230, durationMinutes = 30, reflection = "Good chapter" }))
            .ReadAsync<ReadingSessionDto>();
        Assert.Equal(50, session.PagesRead);

        var afterSession = await (await client.GetAsync($"/api/v1/library/{entry.Id}")).ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(230, afterSession.CurrentPage);

        var finished = await (await client.PutJsonAsync($"/api/v1/library/{entry.Id}/status", new { status = "Read" })).ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(ReadingStatus.Read, finished.Status);
        Assert.NotNull(finished.CompletedOn);
        Assert.Equal(464, finished.CurrentPage);

        var history = await (await client.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<ReadingSessionDto>>();
        Assert.Equal(2, history.TotalCount);
        Assert.Equal(230, history.Items.Sum(s => s.PagesRead));
    }

    [Fact]
    public async Task Invalid_progress_and_sessions_are_rejected_with_problem_details()
    {
        var user = await factory.CreateUserAsync();
        var entry = await AddAsync(user.Client, FakeCatalogProvider.Refactoring);

        var tooFar = await user.Client.PutJsonAsync($"/api/v1/library/{entry.Id}/progress", new { currentPage = 9999 });
        Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
        var problem = JsonDocument.Parse(await tooFar.Content.ReadAsStringAsync()).RootElement;
        Assert.True(problem.GetProperty("errors").TryGetProperty("currentPage", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));

        var negative = await user.Client.PutJsonAsync($"/api/v1/library/{entry.Id}/progress", new { currentPage = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var backwards = await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 50, endPage = 10, durationMinutes = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        var negativeDuration = await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 10, durationMinutes = -5 });
        Assert.Equal(HttpStatusCode.BadRequest, negativeDuration.StatusCode);

        var ok = await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 10, durationMinutes = 5 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var duplicate = await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 10, durationMinutes = 5 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var malformed = await user.Client.PostAsync($"/api/v1/library/{entry.Id}/sessions", new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var unknown = await user.Client.GetAsync($"/api/v1/library/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Catalogue_books_are_shared_without_duplicates_across_users()
    {
        var alice = await factory.CreateUserAsync();
        var bob = await factory.CreateUserAsync();

        var aliceEntry = await AddAsync(alice.Client, FakeCatalogProvider.CleanArchitecture);
        var bobEntry = await AddAsync(bob.Client, FakeCatalogProvider.CleanArchitecture);

        Assert.Equal(aliceEntry.BookId, bobEntry.BookId);
        Assert.NotEqual(aliceEntry.Id, bobEntry.Id);
    }

    [Fact]
    public async Task Manual_books_are_private_and_matched_to_catalogue_by_isbn()
    {
        var alice = await factory.CreateUserAsync();
        var bob = await factory.CreateUserAsync();

        var manual = await (await alice.Client.PostJsonAsync("/api/v1/library/manual", new { title = "My Private Diary", authors = new[] { "Alice" }, pageCount = 120 }))
            .ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/books/{manual.BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/v1/books/{manual.BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PostJsonAsync("/api/v1/library", new { bookId = manual.BookId })).StatusCode);

        // A manual entry with a known ISBN reuses the shared catalogue record.
        var catalogEntry = await AddAsync(alice.Client, FakeCatalogProvider.CleanCode);
        var viaIsbn = await bob.Client.PostJsonAsync("/api/v1/library/manual", new { title = "clean code (my copy)", isbn = "978-0-13-235088-4" });
        Assert.Equal(catalogEntry.BookId, (await viaIsbn.ReadAsync<LibraryEntryDetailsDto>()).BookId);

        var badIsbn = await bob.Client.PostJsonAsync("/api/v1/library/manual", new { title = "Bad", isbn = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, badIsbn.StatusCode);
        var badCover = await bob.Client.PostJsonAsync("/api/v1/library/manual", new { title = "Bad", coverUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, badCover.StatusCode);
    }

    [Fact]
    public async Task Library_filtering_sorting_and_pagination()
    {
        var user = await factory.CreateUserAsync();
        var a = await AddAsync(user.Client, FakeCatalogProvider.CleanCode, ReadingStatus.Read);
        await AddAsync(user.Client, FakeCatalogProvider.Refactoring);
        await AddAsync(user.Client, FakeCatalogProvider.CleanArchitecture, ReadingStatus.CurrentlyReading);
        await user.Client.PutJsonAsync($"/api/v1/library/{a.Id}/review", new { rating = 5, text = (string?)null, visibility = "Private" });
        await user.Client.PutJsonAsync($"/api/v1/library/{a.Id}/favorite", new { isFavorite = true });

        var page1 = await (await user.Client.GetAsync("/api/v1/library?sort=Title&descending=false&pageSize=2")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(["Clean Architecture", "Clean Code"], page1.Items.Select(i => i.Title));
        var page2 = await (await user.Client.GetAsync("/api/v1/library?sort=Title&descending=false&pageSize=2&page=2")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal("Refactoring", Assert.Single(page2.Items).Title);

        var read = await (await user.Client.GetAsync("/api/v1/library?status=Read")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal("Clean Code", Assert.Single(read.Items).Title);

        var byAuthor = await (await user.Client.GetAsync("/api/v1/library?author=martin%20fowler")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal("Refactoring", Assert.Single(byAuthor.Items).Title);

        var search = await (await user.Client.GetAsync("/api/v1/library?search=architecture")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Single(search.Items);

        var favourites = await (await user.Client.GetAsync("/api/v1/library?favorites=true&minRating=4")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal(a.Id, Assert.Single(favourites.Items).Id);

        var byGenre = await (await user.Client.GetAsync("/api/v1/library?genre=Software%20Development")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal(3, byGenre.TotalCount);

        var counts = await (await user.Client.GetAsync("/api/v1/library/counts")).ReadAsync<StatusCountsDto>();
        Assert.Equal(3, counts.Total);
        Assert.Equal(1, counts.Read);
    }

    [Fact]
    public async Task Notes_can_be_created_searched_edited_and_deleted()
    {
        var user = await factory.CreateUserAsync();
        var entry = await AddAsync(user.Client, FakeCatalogProvider.CleanCode);

        var note = await (await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "Quote", content = "Leave the campground cleaner.", pageNumber = 14 }))
            .ReadAsync<NoteDto>();
        await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "ActionItem", content = "Rename variables in module X" });

        var found = await (await user.Client.GetAsync("/api/v1/notes?search=CAMPGROUND")).ReadAsync<PagedResult<NoteDto>>();
        Assert.Equal(note.Id, Assert.Single(found.Items).Id);
        var actions = await (await user.Client.GetAsync("/api/v1/notes?type=ActionItem")).ReadAsync<PagedResult<NoteDto>>();
        Assert.Single(actions.Items);

        var edited = await (await user.Client.PutJsonAsync($"/api/v1/notes/{note.Id}", new { type = "Takeaway", content = "Boy-scout rule" })).ReadAsync<NoteDto>();
        Assert.Equal(NoteType.Takeaway, edited.Type);

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "General", content = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/v1/notes/{note.Id}")).StatusCode);
        Assert.Equal(1, (await (await user.Client.GetAsync("/api/v1/notes")).ReadAsync<PagedResult<NoteDto>>()).TotalCount);
    }

    [Fact]
    public async Task Public_reviews_are_listed_but_private_reviews_and_notes_are_not()
    {
        var alice = await factory.CreateUserAsync();
        var bob = await factory.CreateUserAsync();
        var aliceEntry = await AddAsync(alice.Client, FakeCatalogProvider.Refactoring, ReadingStatus.Read);
        var bobEntry = await AddAsync(bob.Client, FakeCatalogProvider.Refactoring, ReadingStatus.Read);
        await alice.Client.PostJsonAsync($"/api/v1/library/{aliceEntry.Id}/notes", new { type = "General", content = "SECRET-NOTE-123" });

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutJsonAsync($"/api/v1/library/{aliceEntry.Id}/review", new { rating = 5, text = "Alice public review", visibility = "Public" })).StatusCode);
        await bob.Client.PutJsonAsync($"/api/v1/library/{bobEntry.Id}/review", new { rating = 2, text = "Bob private review", visibility = "Private" });

        var anonymous = factory.CreateClient();
        var reviewsJson = await (await anonymous.GetAsync($"/api/v1/books/{aliceEntry.BookId}/reviews")).Content.ReadAsStringAsync();
        Assert.Contains("Alice public review", reviewsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob private review", reviewsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-NOTE-123", reviewsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(alice.Email, reviewsJson, StringComparison.Ordinal);

        var bookJson = await (await anonymous.GetAsync($"/api/v1/books/{aliceEntry.BookId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-NOTE-123", bookJson, StringComparison.Ordinal);

        var invalid = await alice.Client.PutJsonAsync($"/api/v1/library/{aliceEntry.Id}/review", new { rating = 9, text = "x", visibility = "Private" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var publicWithoutText = await alice.Client.PutJsonAsync($"/api/v1/library/{aliceEntry.Id}/review", new { rating = 4, text = "", visibility = "Public" });
        Assert.Equal(HttpStatusCode.BadRequest, publicWithoutText.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/library/{aliceEntry.Id}/review")).StatusCode);
        var mine = await (await alice.Client.GetAsync("/api/v1/reviews/mine")).ReadAsync<PagedResult<MyReviewDto>>();
        var kept = Assert.Single(mine.Items);
        Assert.Equal(5, kept.Rating);
        Assert.Null(kept.Review);
    }

    [Fact]
    public async Task Dashboard_and_goal_statistics_come_from_real_records()
    {
        var user = await factory.CreateUserAsync();
        var empty = await (await user.Client.GetAsync("/api/v1/dashboard")).ReadAsync<DashboardDto>();
        Assert.Equal(0, empty.Counts.Total);
        Assert.Equal(0, empty.PagesReadAllTime);
        Assert.Equal(0, empty.Streak.Current);
        Assert.Empty(empty.TopGenres);

        var year = empty.Today.Year;
        Assert.Equal(HttpStatusCode.OK, (await user.Client.PutJsonAsync($"/api/v1/goals/{year}", new { targetBooks = 24 })).StatusCode);

        var reading = await AddAsync(user.Client, FakeCatalogProvider.CleanCode, ReadingStatus.CurrentlyReading);
        await user.Client.PostJsonAsync($"/api/v1/library/{reading.Id}/sessions", new { startPage = 0, endPage = 40, durationMinutes = 30 });
        var finished = await AddAsync(user.Client, FakeCatalogProvider.Refactoring, ReadingStatus.CurrentlyReading);
        await user.Client.PutJsonAsync($"/api/v1/library/{finished.Id}/status", new { status = "Read" });

        // Editing the completion date must not count the book twice.
        await user.Client.PutJsonAsync($"/api/v1/library/{finished.Id}/details", new { totalPages = 448, startedOn = empty.Today, completedOn = empty.Today, privateNotes = "ok" });

        var goal = await (await user.Client.GetAsync($"/api/v1/goals/{year}")).ReadAsync<GoalProgressDto>();
        Assert.True(goal.HasGoal);
        Assert.Equal(24, goal.TargetBooks);
        Assert.Equal(1, goal.CompletedBooks);
        Assert.Equal(23, goal.RemainingBooks);
        Assert.Equal(40, goal.PagesRead);

        var dashboard = await (await user.Client.GetAsync("/api/v1/dashboard")).ReadAsync<DashboardDto>();
        Assert.Equal(2, dashboard.Counts.Total);
        Assert.Equal(1, dashboard.Counts.Read);
        Assert.Equal(40, dashboard.PagesReadAllTime);
        Assert.Equal(1, dashboard.Streak.Current);
        Assert.Equal(12, dashboard.MonthlyActivity.Count);
        Assert.Equal(40, dashboard.MonthlyActivity[^1].Pages);
        Assert.Equal(1, dashboard.MonthlyActivity[^1].BooksCompleted);

        var analytics = await (await user.Client.GetAsync($"/api/v1/analytics/{year}")).ReadAsync<YearAnalyticsDto>();
        Assert.Equal(1, analytics.BooksCompleted);
        Assert.Equal(40, analytics.PagesRead);
        Assert.Equal(1, analytics.ReadingDays);

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutJsonAsync($"/api/v1/goals/{year}", new { targetBooks = 0 })).StatusCode);
    }

    [Fact]
    public async Task Removing_an_entry_keeps_the_catalogue_book_and_deletes_personal_history()
    {
        var user = await factory.CreateUserAsync();
        var entry = await AddAsync(user.Client, FakeCatalogProvider.CleanArchitecture, ReadingStatus.CurrentlyReading);
        await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 5, durationMinutes = 5 });

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/v1/library/{entry.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/v1/library/{entry.Id}")).StatusCode);
        Assert.Equal(0, (await (await user.Client.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<ReadingSessionDto>>()).TotalCount);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync($"/api/v1/books/{entry.BookId}")).StatusCode);
    }

    [Fact]
    public async Task Provider_outage_returns_503_and_saved_books_stay_available()
    {
        var user = await factory.CreateUserAsync();
        var entry = await AddAsync(user.Client, FakeCatalogProvider.CleanCode);
        factory.Catalog.Unavailable = true;
        try
        {
            var search = await user.Client.GetAsync($"/api/v1/catalog/search?q=outage-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
            Assert.Contains("unavailable", await search.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync($"/api/v1/library/{entry.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync($"/api/v1/books/{entry.BookId}")).StatusCode);
        }
        finally
        {
            factory.Catalog.Unavailable = false;
        }
    }
}
