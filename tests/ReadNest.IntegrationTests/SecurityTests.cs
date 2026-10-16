using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Application.Notes;
using ReadNest.Application.Reading;
using ReadNest.Application.Social;
using ReadNest.Domain.Enums;
using ReadNest.Infrastructure.Identity;
using ReadNest.Infrastructure.Persistence;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.IntegrationTests;

/// <summary>Verifies that one user can never read or change another user's private records.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecurityTests(ReadNestFactory factory)
{
    private async Task<(TestUser Alice, TestUser Bob, LibraryEntryDetailsDto Entry, NoteDto Note, ReadingSessionDto Session)> SetupAsync()
    {
        var alice = await factory.CreateUserAsync();
        var bob = await factory.CreateUserAsync();
        var entry = await (await alice.Client.PostJsonAsync("/api/v1/library", new { provider = "GoogleBooks", externalId = "clean-code", status = "CurrentlyReading" }))
            .ReadAsync<LibraryEntryDetailsDto>();
        var note = await (await alice.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "General", content = "alice-private" }))
            .ReadAsync<NoteDto>();
        var session = await (await alice.Client.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 0, endPage = 10, durationMinutes = 5 }))
            .ReadAsync<ReadingSessionDto>();
        return (alice, bob, entry, note, session);
    }

    [Fact]
    public async Task Other_users_cannot_read_or_modify_library_entries()
    {
        var (alice, bob, entry, note, session) = await SetupAsync();
        var c = bob.Client;

        var attempts = new List<HttpResponseMessage>
        {
            await c.GetAsync($"/api/v1/library/{entry.Id}"),
            await c.PutJsonAsync($"/api/v1/library/{entry.Id}/status", new { status = "Read" }),
            await c.PutJsonAsync($"/api/v1/library/{entry.Id}/progress", new { currentPage = 1 }),
            await c.PutJsonAsync($"/api/v1/library/{entry.Id}/details", new { totalPages = 10 }),
            await c.PutJsonAsync($"/api/v1/library/{entry.Id}/favorite", new { isFavorite = true }),
            await c.PutJsonAsync($"/api/v1/library/{entry.Id}/review", new { rating = 1, text = "x", visibility = "Public" }),
            await c.DeleteAsync($"/api/v1/library/{entry.Id}/review"),
            await c.PostJsonAsync($"/api/v1/library/{entry.Id}/sessions", new { startPage = 10, endPage = 20, durationMinutes = 1 }),
            await c.PostJsonAsync($"/api/v1/library/{entry.Id}/notes", new { type = "General", content = "injected" }),
            await c.PutJsonAsync($"/api/v1/notes/{note.Id}", new { type = "General", content = "hacked" }),
            await c.DeleteAsync($"/api/v1/notes/{note.Id}"),
            await c.PutJsonAsync($"/api/v1/sessions/{session.Id}", new { startPage = 0, endPage = 1, durationMinutes = 1 }),
            await c.DeleteAsync($"/api/v1/sessions/{session.Id}"),
            await c.DeleteAsync($"/api/v1/library/{entry.Id}"),
        };

        Assert.All(attempts, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));

        var aliceView = await (await alice.Client.GetAsync($"/api/v1/library/{entry.Id}")).ReadAsync<LibraryEntryDetailsDto>();
        Assert.Equal(ReadingStatus.CurrentlyReading, aliceView.Status);
        Assert.Equal(10, aliceView.CurrentPage);
        Assert.Equal(1, aliceView.NoteCount);
        Assert.Equal(1, aliceView.SessionCount);
        Assert.Null(aliceView.Rating);
    }

    [Fact]
    public async Task Lists_searches_and_exports_only_contain_the_callers_data()
    {
        var (_, bob, _, _, _) = await SetupAsync();

        Assert.Equal(0, (await (await bob.Client.GetAsync("/api/v1/library")).ReadAsync<PagedResult<LibraryEntryDto>>()).TotalCount);
        Assert.Equal(0, (await (await bob.Client.GetAsync("/api/v1/notes?search=alice")).ReadAsync<PagedResult<NoteDto>>()).TotalCount);
        Assert.Equal(0, (await (await bob.Client.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<ReadingSessionDto>>()).TotalCount);
        var export = await (await bob.Client.GetAsync("/api/v1/export/library.json")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice-private", export, StringComparison.Ordinal);
        Assert.DoesNotContain("Clean Code", export, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shelves_validate_ownership_of_both_shelf_and_entry()
    {
        var (alice, bob, entry, _, _) = await SetupAsync();
        var aliceShelf = await factory.WithServiceAsync<Guid, ShelfService>(s => s.CreateAsync(alice.Id, new ShelfRequest("Favourites", null, false), CancellationToken.None));
        var bobShelf = await factory.WithServiceAsync<Guid, ShelfService>(s => s.CreateAsync(bob.Id, new ShelfRequest("Mine", null, false), CancellationToken.None));

        // Bob cannot put Alice's entry on his shelf, nor touch Alice's shelf.
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<ShelfService>(s => s.AddEntryAsync(bob.Id, bobShelf, entry.Id, CancellationToken.None)));
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<ShelfService>(s => s.DeleteAsync(bob.Id, aliceShelf, CancellationToken.None)));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PutAsync($"/api/v1/shelves/{aliceShelf}/entries/{entry.Id}", null)).StatusCode);

        // Alice can, idempotently; deleting the shelf keeps the book in her library.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsync($"/api/v1/shelves/{aliceShelf}/entries/{entry.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsync($"/api/v1/shelves/{aliceShelf}/entries/{entry.Id}", null)).StatusCode);
        var onShelf = await (await alice.Client.GetAsync($"/api/v1/library?shelfId={aliceShelf}")).ReadAsync<PagedResult<LibraryEntryDto>>();
        Assert.Single(onShelf.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.PostJsonAsync("/api/v1/shelves", new { name = "favourites", isPublic = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/shelves/{aliceShelf}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/library/{entry.Id}")).StatusCode);
    }

    [Fact]
    public async Task Private_profiles_and_shelves_are_hidden_until_made_public()
    {
        var (alice, bob, entry, _, _) = await SetupAsync();
        var profile = await factory.WithServiceAsync<ProfileDto, ProfileService>(s => s.GetOrCreateAsync(alice.Id, "Alice", null, CancellationToken.None));
        var shelfId = await factory.WithServiceAsync<Guid, ShelfService>(s => s.CreateAsync(alice.Id, new ShelfRequest("Shared list", null, false), CancellationToken.None));
        await factory.WithServiceAsync<ShelfService>(s => s.AddEntryAsync(alice.Id, shelfId, entry.Id, CancellationToken.None));
        var shelf = await factory.WithServiceAsync<ShelfDto, ShelfService>(s => s.GetAsync(alice.Id, shelfId, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<PublicProfileDto, SocialService>(s => s.GetPublicProfileAsync(profile.Handle, bob.Id, CancellationToken.None)));
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<SharedShelfDto, ShelfService>(s => s.GetSharedAsync(shelf.ShareSlug, CancellationToken.None)));
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<SocialService>(s => s.FollowAsync(bob.Id, profile.Handle, CancellationToken.None)));
        var hiddenPage = await (await factory.CreateClient().GetAsync("/s/" + shelf.ShareSlug)).Content.ReadAsStringAsync();
        Assert.Contains("This shelf isn&#x27;t available", hiddenPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Clean Code", hiddenPage, StringComparison.Ordinal);

        await factory.WithServiceAsync<ProfileService>(s => s.UpdatePrivacyAsync(alice.Id, new PrivacyRequest(true, false), CancellationToken.None));
        await factory.WithServiceAsync<ShelfService>(s => s.UpdateAsync(alice.Id, shelfId, new ShelfRequest("Shared list", null, true), CancellationToken.None));

        var visible = await factory.WithServiceAsync<PublicProfileDto, SocialService>(s => s.GetPublicProfileAsync(profile.Handle, bob.Id, CancellationToken.None));
        Assert.Null(visible.BooksRead);
        Assert.Empty(visible.CurrentlyReading);
        await Assert.ThrowsAsync<NotFoundException>(() => factory.WithServiceAsync<PagedResult<PublicBookDto>, SocialService>(s => s.GetPublicLibraryAsync(profile.Handle, bob.Id, null, 1, CancellationToken.None)));

        var shared = await factory.WithServiceAsync<SharedShelfDto, ShelfService>(s => s.GetSharedAsync(shelf.ShareSlug, CancellationToken.None));
        Assert.Equal("Clean Code", Assert.Single(shared.Books).Title);
        var sharedPage = await (await factory.CreateClient().GetAsync("/s/" + shelf.ShareSlug)).Content.ReadAsStringAsync();
        Assert.Contains("Shared list", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("alice-private", sharedPage, StringComparison.Ordinal);

        await factory.WithServiceAsync<SocialService>(s => s.FollowAsync(bob.Id, profile.Handle, CancellationToken.None));
        await factory.WithServiceAsync<SocialService>(s => s.FollowAsync(bob.Id, profile.Handle, CancellationToken.None));
        var followers = await factory.WithServiceAsync<IReadOnlyList<PersonDto>, SocialService>(s => s.GetFollowersAsync(alice.Id, CancellationToken.None));
        Assert.Single(followers);
        await Assert.ThrowsAsync<RequestValidationException>(() => factory.WithServiceAsync<SocialService>(s => s.FollowAsync(alice.Id, profile.Handle, CancellationToken.None)));
    }

    [Fact]
    public async Task Lent_books_cannot_be_removed_until_returned()
    {
        var (alice, _, entry, _, _) = await SetupAsync();
        await factory.WithServiceAsync<CollectionService>(s => s.UpdateOwnershipAsync(alice.Id, entry.Id, new OwnershipRequest(OwnershipStatus.Owned, BookFormat.Physical, null, 30m, null), CancellationToken.None));
        var loanId = await factory.WithServiceAsync<Guid, CollectionService>(s => s.LendAsync(alice.Id, entry.Id, new LoanRequest("Sam", null, null, null), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.DeleteAsync($"/api/v1/library/{entry.Id}")).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => factory.WithServiceAsync<Guid, CollectionService>(s => s.LendAsync(alice.Id, entry.Id, new LoanRequest("Alex", null, null, null), CancellationToken.None)));

        await factory.WithServiceAsync<CollectionService>(s => s.ReturnAsync(alice.Id, loanId, null, CancellationToken.None));
        var loans = await factory.WithServiceAsync<IReadOnlyList<LoanDto>, CollectionService>(s => s.GetLoansAsync(alice.Id, false, entry.Id, CancellationToken.None));
        Assert.NotNull(Assert.Single(loans).ReturnedOn);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/library/{entry.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_removes_all_personal_data()
    {
        var (alice, _, entry, _, _) = await SetupAsync();
        await factory.WithServiceAsync<Guid, ShelfService>(s => s.CreateAsync(alice.Id, new ShelfRequest("Gone", null, true), CancellationToken.None));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.DeleteAsync((await users.FindByIdAsync(alice.Id.ToString()))!);
            Assert.True(result.Succeeded);
        }

        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ReadNestDbContext>();
        Assert.False(await db.UserBooks.AnyAsync(ub => ub.UserId == alice.Id));
        Assert.False(await db.ReadingNotes.AnyAsync(n => n.UserBookId == entry.Id));
        Assert.False(await db.CustomShelves.AnyAsync(s => s.UserId == alice.Id));
        Assert.False(await db.UserProfiles.AnyAsync(p => p.UserId == alice.Id));
        Assert.True(await db.Books.AnyAsync(b => b.Id == entry.BookId), "Shared catalogue records must survive account deletion.");
    }
}
