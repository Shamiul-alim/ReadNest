using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReadNest.Application.Common;
using ReadNest.Application.ImportExport;
using ReadNest.Application.Library;
using ReadNest.Application.Recommendations;
using ReadNest.Application.Social;
using ReadNest.Domain.Enums;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ImportExportAndFeatureTests(ReadNestFactory factory)
{
    private static MultipartFormDataContent CsvUpload(string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "library.csv" } };
    }

    [Fact]
    public async Task Csv_import_validates_rows_skips_duplicates_and_reports_a_summary()
    {
        var user = await factory.CreateUserAsync();
        const string csv = """
            Title,Author,ISBN,Status,CurrentPage,TotalPages,Rating,StartedOn,CompletedOn,Favorite,Shelves
            Clean Code,Robert C. Martin,9780132350884,Read,,464,5,2025-01-01,2025-02-01,true,Software; Classics
            My Notebook,Me,,Currently Reading,30,100,,,,,
            "Quoted, Title",Someone,,,,,,,,,
            Bad Rating,X,,,,,9,,,,
            Bad Isbn,X,123,,,,,,,,
            Clean Code duplicate,Robert C. Martin,0132350882,,,,,,,,
            ,Missing Title,,,,,,,,,
            Future,X,,Read,,,,,2999-01-01,,
            """;

        var response = await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv));
        var summary = await response.ReadAsync<ImportSummary>();

        Assert.Equal(8, summary.TotalRows);
        Assert.Equal(3, summary.Imported);
        Assert.Single(summary.Skipped);
        Assert.Equal(4, summary.Errors.Count);
        Assert.Contains(summary.Errors, e => e.Errors.Any(m => m.Contains("Rating", StringComparison.Ordinal)));

        var library = await (await user.Client.GetAsync("/api/v1/library?sort=Title&descending=false")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal(3, library.TotalCount);
        var cleanCode = library.Items.Single(i => i.Title == "Clean Code");
        Assert.Equal(ReadingStatus.Read, cleanCode.Status);
        Assert.Equal(new DateOnly(2025, 2, 1), cleanCode.CompletedOn);
        Assert.True(cleanCode.IsFavorite);
        var notebook = library.Items.Single(i => i.Title == "My Notebook");
        Assert.Equal(30, notebook.CurrentPage);

        // Imports never fabricate reading sessions.
        var sessions = await (await user.Client.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<ReadNest.Application.Reading.ReadingSessionDto>>();
        Assert.Equal(0, sessions.TotalCount);

        // Re-importing the same file creates nothing new.
        var second = await (await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv))).ReadAsync<ImportSummary>();
        Assert.Equal(0, second.Imported);
    }

    [Fact]
    public async Task Malformed_or_oversized_csv_is_rejected()
    {
        var user = await factory.CreateUserAsync();

        var malformed = await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload("Title\n\"unterminated"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var noTitle = await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload("Name,Author\nX,Y"));
        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);

        var huge = await user.Client.PostAsync("/api/v1/import/library.csv", CsvUpload("Title\n" + new string('a', 1_100_000)));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
    }

    [Fact]
    public async Task Csv_export_is_safe_against_formula_injection_and_round_trips()
    {
        var user = await factory.CreateUserAsync();
        await user.Client.PostJsonAsync("/api/v1/library/manual", new { title = "=HYPERLINK(\"http://evil.test\")", authors = new[] { "@attacker" } });

        var export = await user.Client.GetAsync("/api/v1/export/library.csv");
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", export.Content.Headers.ContentDisposition?.ToString() ?? "", StringComparison.Ordinal);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.test\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("'@attacker", csv, StringComparison.Ordinal);

        var other = await factory.CreateUserAsync();
        var roundTrip = await (await other.Client.PostAsync("/api/v1/import/library.csv", CsvUpload(csv.TrimStart('﻿')))).ReadAsync<ImportSummary>();
        Assert.Equal(1, roundTrip.Imported);
        var imported = await (await other.Client.GetAsync("/api/v1/library")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Equal("=HYPERLINK(\"http://evil.test\")", Assert.Single(imported.Items).Title);
    }

    [Fact]
    public async Task Json_export_contains_the_users_full_reading_data()
    {
        var user = await factory.CreateUserAsync();
        var entry = await (await user.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "refactoring", status = "CurrentlyReading" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "Lesson", content = "Small steps" });
        await user.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 12, durationMinutes = 20 });

        var json = JsonDocument.Parse(await (await user.Client.GetAsync("/api/v1/export/library.json")).Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("readnest-export", json.GetProperty("format").GetString());
        var item = json.GetProperty("library")[0];
        Assert.Equal("Refactoring", item.GetProperty("book").GetProperty("title").GetString());
        Assert.Equal("Small steps", item.GetProperty("notes")[0].GetProperty("content").GetString());
        Assert.Equal(12, item.GetProperty("sessions")[0].GetProperty("pagesRead").GetInt32());
    }

    [Fact]
    public async Task Recommendations_use_library_signals_and_exclude_owned_books()
    {
        var seeder = await factory.CreateUserAsync();
        foreach (var id in new[] { "clean-code", "clean-architecture", "refactoring" })
        {
            await seeder.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = id });
        }

        var reader = await factory.CreateUserAsync();
        var none = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();
        Assert.Empty(none.Items);

        var loved = await (await reader.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "Read" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        await reader.Client.PutJsonAsync($"/api/v1/library/{loved.Id}/review", new { rating = 5, text = (string?)null, visibility = "Private" });

        var recommendations = await (await reader.Client.GetAsync("/api/v1/recommendations")).ReadAsync<RecommendationsDto>();

        Assert.NotEmpty(recommendations.Items);
        Assert.DoesNotContain(recommendations.Items, r => r.Book.Id == loved.BookId);
        var top = recommendations.Items[0];
        Assert.Equal("Clean Architecture", top.Book.Title);
        Assert.Equal("Recommended because you enjoy books by Robert C. Martin.", top.Reason);
        Assert.Contains("Robert C. Martin", recommendations.Topics.Authors);
    }

    [Fact]
    public async Task Challenge_progress_counts_books_finished_inside_the_window()
    {
        var owner = await factory.CreateUserAsync();
        var participant = await factory.CreateUserAsync();
        var today = await factory.WithServiceAsync<DateOnly, LibraryService>(s => s.GetTodayAsync(owner.Id, CancellationToken.None));
        var challengeId = await factory.WithServiceAsync<Guid, ChallengeService>(s => s.CreateAsync(
            owner.Id, new ChallengeRequest("Sprint", null, 2, today.AddDays(-7), today.AddDays(30), true), CancellationToken.None));

        await factory.WithServiceAsync<ChallengeService>(s => s.JoinAsync(participant.Id, challengeId, CancellationToken.None));
        var entry = await (await participant.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "refactoring", status = "Read" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(today, entry.CompletedOn);

        var details = await factory.WithServiceAsync<ChallengeDetailsDto, ChallengeService>(s => s.GetAsync(challengeId, participant.Id, CancellationToken.None));
        Assert.Equal(2, details.Challenge.ParticipantCount);
        Assert.Equal(1, details.MyBooksCompleted);
        Assert.Contains(details.Participants, p => p.DisplayName == "Private reader");

        await Assert.ThrowsAsync<ConflictException>(() => factory.WithServiceAsync<ChallengeService>(s => s.LeaveAsync(owner.Id, challengeId, CancellationToken.None)));
        await factory.WithServiceAsync<ChallengeService>(s => s.LeaveAsync(participant.Id, challengeId, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<ChallengeService>(s => s.DeleteAsync(participant.Id, challengeId, CancellationToken.None)));
    }
}
