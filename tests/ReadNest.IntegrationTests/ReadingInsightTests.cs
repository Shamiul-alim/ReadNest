using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReadNest.Application.Analytics;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Application.Reading;
using ReadNest.Application.Recommendations;
using ReadNest.Domain.Enums;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.IntegrationTests;

/// <summary>Statistics, undo and "Not for me" behaviour used by the redesigned pages.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReadingInsightTests(ReadNestFactory factory)
{
    private static MultipartFormDataContent CsvUpload(string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "library.csv" } };
    }

    [Fact]
    public async Task Taste_statistics_work_once_a_reader_has_enough_books()
    {
        // Regression: ordering by members of a record built in the projection could not be translated by EF Core,
        // which broke the dashboard and analytics for every reader with three or more qualifying books.
        var user = await factory.CreateUserAsync();
        var today = await factory.WithServiceAsync<DateOnly, LibraryService>(s => s.GetTodayAsync(user.Id, CancellationToken.None));
        var done = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var csv = $"""
            Title,Author,ISBN,Status,CurrentPage,TotalPages,Rating,StartedOn,CompletedOn,Favorite,Shelves
            First,Ann Author,,Read,,100,5,{done},{done},,
            Second,Ann Author,,Read,,120,4,{done},{done},,
            Third,Ann Author,,Read,,140,5,{done},{done},,
            Fourth,Other Writer,,Read,,90,3,{done},{done},,
            """;
        (await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv))).EnsureSuccessStatusCode();

        var dashboard = JsonDocument.Parse(await (await user.Client.GetAsync("/api/v1/dashboard")).ReadSuccessStringAsync()).RootElement;
        var topAuthor = dashboard.GetProperty("topAuthors")[0];
        Assert.Equal("Ann Author", topAuthor.GetProperty("name").GetString());
        Assert.Equal(3, topAuthor.GetProperty("count").GetInt32());

        var year = JsonDocument.Parse(await (await user.Client.GetAsync($"/api/v1/analytics/{today.Year}")).ReadSuccessStringAsync()).RootElement;
        Assert.Equal(4, year.GetProperty("booksCompleted").GetInt32());
        Assert.Equal("Ann Author", year.GetProperty("topAuthors")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Csv_import_keeps_paused_status_and_records_no_reading_activity()
    {
        // Regression: the imported page number promoted Paused books to Reading and stamped "last read" with the import date.
        var user = await factory.CreateUserAsync();
        const string csv = """
            Title,Author,ISBN,Status,CurrentPage,TotalPages,Rating,StartedOn,CompletedOn,Favorite,Shelves
            On Hold,Some Author,,Paused,210,502,,2026-01-10,,,
            Reading Now,Some Author,,Currently reading,40,300,,2026-01-10,,,
            """;
        (await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv))).EnsureSuccessStatusCode();

        var library = await (await user.Client.GetAsync("/api/v1/library")).ReadAsync<PagedResult<LibraryEntryDto>>();
        var paused = library.Items.Single(e => e.Title == "On Hold");
        Assert.Equal(ReadingStatus.Paused, paused.Status);
        Assert.Equal(210, paused.CurrentPage);
        Assert.Null(paused.LastReadOn);
        var reading = library.Items.Single(e => e.Title == "Reading Now");
        Assert.Equal(ReadingStatus.CurrentlyReading, reading.Status);
        Assert.Equal(40, reading.CurrentPage);
        Assert.Null(reading.LastReadOn);
    }

    [Fact]
    public async Task Not_for_me_hides_a_suggestion_for_that_reader_only_until_restored()
    {
        var seeder = await factory.CreateUserAsync();
        foreach (var id in new[] { "clean-code", "clean-architecture", "refactoring" })
        {
            await seeder.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = id });
        }

        var reader = await factory.CreateUserAsync();
        var other = await factory.CreateUserAsync();
        foreach (var user in new[] { reader, other })
        {
            var loved = await (await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "Read" }))
                .ReadAsync<LibraryEntryDetailsDto>();
            await user.Client.PutJsonAsync($"/api/v1/library/{loved.Id}/review", new { rating = 5, text = (string?)null, visibility = "Private" });
        }

        var before = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        var target = before.Items.Single(r => r.Book.Title == "Clean Architecture");
        Assert.Equal(ReasonKind.Author, target.Basis);

        var dismiss = await reader.Client.PostAsync($"/api/v1/recommendations/{target.Book.Id}/dismiss", null);
        Assert.Equal(HttpStatusCode.NoContent, dismiss.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reader.Client.PostAsync($"/api/v1/recommendations/{target.Book.Id}/dismiss", null)).StatusCode);

        var after = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.DoesNotContain(after.Items, r => r.Book.Id == target.Book.Id);
        Assert.Equal(1, after.DismissedCount);

        // Another reader's suggestions are unaffected.
        var others = await (await other.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.Contains(others.Items, r => r.Book.Id == target.Book.Id);
        Assert.Equal(0, others.DismissedCount);

        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.PostAsync($"/api/v1/recommendations/{Guid.NewGuid()}/dismiss", null)).StatusCode);

        var restored = await (await reader.Client.DeleteAsync("/api/v1/recommendations/dismissals")).ReadAsync<int>();
        Assert.Equal(1, restored);
        var again = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.Contains(again.Items, r => r.Book.Id == target.Book.Id);
    }

    [Fact]
    public async Task Books_the_reader_already_has_in_another_edition_are_not_recommended()
    {
        var seeder = await factory.CreateUserAsync();
        foreach (var id in new[] { "clean-code", "clean-architecture" })
        {
            await seeder.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = id });
        }

        var reader = await factory.CreateUserAsync();
        var loved = await (await reader.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "Read" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        await reader.Client.PutJsonAsync($"/api/v1/library/{loved.Id}/review", new { rating = 5, text = (string?)null, visibility = "Private" });
        var withoutCopy = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.Contains(withoutCopy.Items, r => r.Book.Title == "Clean Architecture");

        // A private manual record of the same title and author (no ISBN) means the reader already has it.
        const string csv = """
            Title,Author,ISBN,Status
            Clean Architecture,Robert C. Martin,,Want to read
            """;
        (await reader.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv))).EnsureSuccessStatusCode();

        var withCopy = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.DoesNotContain(withCopy.Items, r => r.Book.Title == "Clean Architecture");
    }

    [Fact]
    public async Task Undoing_a_progress_update_restores_page_status_and_removes_the_recorded_session()
    {
        var user = await factory.CreateUserAsync();
        var intruder = await factory.CreateUserAsync();
        var entry = await (await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "refactoring", status = "WantToRead" }))
            .ReadAsync<LibraryEntryDetailsDto>();

        var change = await factory.WithServiceAsync<ProgressUpdateResult, LibraryService>(s =>
            s.ApplyProgressAsync(user.Id, entry.Id, new UpdateProgressRequest(40), CancellationToken.None));
        Assert.Equal(40, change.Entry.CurrentPage);
        Assert.Equal(ReadingStatus.CurrentlyReading, change.Entry.Status);
        Assert.Equal(ReadingStatus.WantToRead, change.PreviousStatus);
        Assert.NotNull(change.RecordedSessionId);

        // Another reader cannot undo (or even find) someone else's entry.
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<LibraryEntryDetailsDto, LibraryService>(s =>
            s.UndoProgressAsync(intruder.Id, entry.Id, change, CancellationToken.None)));

        var undone = await factory.WithServiceAsync<LibraryEntryDetailsDto, LibraryService>(s =>
            s.UndoProgressAsync(user.Id, entry.Id, change, CancellationToken.None));
        Assert.Equal(0, undone.CurrentPage);
        Assert.Equal(ReadingStatus.WantToRead, undone.Status);
        var sessions = await (await user.Client.GetAsync($"/api/v1/sessions?entryId={entry.Id}")).ReadAsync<PagedResult<ReadingSessionDto>>();
        Assert.Equal(0, sessions.TotalCount);
    }

    [Fact]
    public async Task Weekly_daily_and_outcome_statistics_come_from_logged_reading()
    {
        var user = await factory.CreateUserAsync();
        var today = await factory.WithServiceAsync<DateOnly, LibraryService>(s => s.GetTodayAsync(user.Id, CancellationToken.None));
        var reading = await (await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "refactoring", status = "CurrentlyReading" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        await user.Client.PostJsonAsync($"/api/v1/library/{reading.Id}/sessions", new { readOn = today, startPage = 0, endPage = 30, durationMinutes = 45 });
        await user.Client.PostJsonAsync($"/api/v1/library/{reading.Id}/sessions", new { readOn = today.AddDays(-20), startPage = 30, endPage = 50, durationMinutes = 25 });
        var abandoned = await (await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "CurrentlyReading" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        await user.Client.PutJsonAsync($"/api/v1/library/{abandoned.Id}/status", new { status = "DidNotFinish" });

        var weeks = await factory.WithServiceAsync<IReadOnlyList<WeeklyPagesDto>, StatisticsService>(s => s.GetWeeklyPagesAsync(user.Id, 12, CancellationToken.None));
        Assert.Equal(12, weeks.Count);
        Assert.Equal(DayOfWeek.Monday, weeks[^1].WeekStart.DayOfWeek);
        Assert.Equal(30, weeks[^1].Pages);
        Assert.Equal(50, weeks.Sum(w => w.Pages));

        var days = await factory.WithServiceAsync<IReadOnlyList<DailyActivityDto>, StatisticsService>(s => s.GetDailyActivityAsync(user.Id, 14, CancellationToken.None));
        Assert.Equal(14, days.Count);
        Assert.Equal(today, days[^1].Date);
        Assert.Equal(45, days[^1].Minutes);
        Assert.Equal(0, days[0].Pages);

        var outcomes = await factory.WithServiceAsync<StartOutcomesDto, StatisticsService>(s => s.GetStartOutcomesAsync(user.Id, today.Year, CancellationToken.None));
        Assert.Equal(1, outcomes.StillReading);
        Assert.Equal(1, outcomes.DidNotFinish);
        Assert.Equal(2, outcomes.Total);
    }
}
