using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Domain.Enums;
using ReadNest.Web.Authorization;

namespace ReadNest.Web.Api;

public sealed record AddLibraryEntryRequest(Guid? BookId, CatalogProvider? Provider, string? ExternalId, ReadingStatus Status = ReadingStatus.WantToRead);

public sealed record AddManualEntryRequest(
    string Title,
    string? Subtitle,
    IReadOnlyList<string>? Authors,
    string? Isbn,
    int? PageCount,
    int? PublishedYear,
    string? Publisher,
    string? LanguageCode,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<string>? Genres,
    ReadingStatus Status = ReadingStatus.WantToRead);

public sealed record FavoriteRequest(bool IsFavorite);

internal static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        var library = api.MapGroup("/library").WithTags("Library");

        library.MapGet("/", async Task<Ok<PagedResult<LibraryEntryDto>>> (
                string? search,
                ReadingStatus? status,
                string? genre,
                string? author,
                int? minRating,
                bool? favorites,
                Guid? shelfId,
                LibrarySort? sort,
                bool? descending,
                int? page,
                int? pageSize,
                ClaimsPrincipal user,
                LibraryService service,
                CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(user.GetRequiredUserId(), new LibraryQuery
            {
                Search = search,
                Status = status,
                Genre = genre,
                Author = author,
                MinRating = minRating,
                FavoritesOnly = favorites ?? false,
                ShelfId = shelfId,
                Sort = sort ?? LibrarySort.LastUpdated,
                Descending = descending ?? true,
                Page = page ?? 1,
                PageSize = pageSize ?? 24,
            }, ct)))
            .WithSummary("List the authenticated user's library with filtering, sorting and pagination.");

        library.MapGet("/counts", async Task<Ok<StatusCountsDto>> (ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetStatusCountsAsync(user.GetRequiredUserId(), ct)))
            .WithSummary("Number of library entries per reading status.");

        library.MapPost("/", async Task<Results<Created<LibraryEntryDetailsDto>, Ok<LibraryEntryDetailsDto>>> (
                AddLibraryEntryRequest request,
                ClaimsPrincipal user,
                LibraryService service,
                BookService books,
                CancellationToken ct) =>
            {
                var userId = user.GetRequiredUserId();
                Guid bookId;
                if (request.BookId is { } id)
                {
                    bookId = id;
                }
                else if (request.Provider is { } provider && !string.IsNullOrWhiteSpace(request.ExternalId))
                {
                    bookId = await books.ImportFromCatalogAsync(provider, request.ExternalId, ct);
                }
                else
                {
                    throw new RequestValidationException("bookId", "Provide either bookId, or provider and externalId.");
                }

                var result = await service.AddBookAsync(userId, new AddToLibraryRequest(bookId, request.Status), ct);
                var entry = await service.GetEntryAsync(userId, result.EntryId, ct);
                return result.AlreadyInLibrary
                    ? TypedResults.Ok(entry)
                    : TypedResults.Created($"/api/v1/library/{entry.Id}", entry);
            })
            .Accepts<AddLibraryEntryRequest>(ApiEndpoints.Json)
            .WithSummary("Add a local or catalogue book to the library. Returns 200 with the existing entry if it is already there.");

        library.MapPost("/manual", async Task<Results<Created<LibraryEntryDetailsDto>, Ok<LibraryEntryDetailsDto>>> (
                AddManualEntryRequest request,
                ClaimsPrincipal user,
                LibraryService service,
                CancellationToken ct) =>
            {
                var userId = user.GetRequiredUserId();
                var input = new ManualBookInput(request.Title, request.Subtitle, request.Authors ?? [], request.Isbn, request.PageCount, request.PublishedYear,
                    request.Publisher, request.LanguageCode, request.Description, request.CoverUrl, request.Genres ?? []);
                var result = await service.AddManualBookAsync(userId, input, request.Status, ct);
                var entry = await service.GetEntryAsync(userId, result.EntryId, ct);
                return result.AlreadyInLibrary ? TypedResults.Ok(entry) : TypedResults.Created($"/api/v1/library/{entry.Id}", entry);
            })
            .WithSummary("Add a manually entered (private) book to the library.");

        library.MapGet("/{entryId:guid}", async Task<Ok<LibraryEntryDetailsDto>> (Guid entryId, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetEntryAsync(user.GetRequiredUserId(), entryId, ct)))
            .WithSummary("Get one library entry.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        library.MapPut("/{entryId:guid}/status", async Task<Ok<LibraryEntryDetailsDto>> (Guid entryId, UpdateStatusRequest request, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateStatusAsync(user.GetRequiredUserId(), entryId, request, ct)))
            .WithSummary("Change the reading status (optionally with a completion date when marking as Read).");

        library.MapPut("/{entryId:guid}/progress", async Task<Ok<LibraryEntryDetailsDto>> (Guid entryId, UpdateProgressRequest request, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateProgressAsync(user.GetRequiredUserId(), entryId, request, ct)))
            .WithSummary("Move the bookmark; forward moves are recorded as today's reading session unless recordSession is false.");

        library.MapPut("/{entryId:guid}/details", async Task<Ok<LibraryEntryDetailsDto>> (Guid entryId, UpdateEntryDetailsRequest request, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateDetailsAsync(user.GetRequiredUserId(), entryId, request, ct)))
            .WithSummary("Update total pages, reading dates and private notes.");

        library.MapPut("/{entryId:guid}/favorite", async Task<NoContent> (Guid entryId, FavoriteRequest request, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
            {
                await service.SetFavoriteAsync(user.GetRequiredUserId(), entryId, request.IsFavorite, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Mark or unmark a favourite.");

        library.MapDelete("/{entryId:guid}", async Task<NoContent> (Guid entryId, ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
            {
                await service.RemoveAsync(user.GetRequiredUserId(), entryId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Remove an entry with its sessions, notes, review and loan history. Refused (409) while the book is lent out.")
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
