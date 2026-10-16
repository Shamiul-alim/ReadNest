using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Application.DTOs;
using ReadNest.Application.Reviews;
using ReadNest.Domain.Enums;
using ReadNest.Web.Authorization;
using ReadNest.Web.Configuration;

namespace ReadNest.Web.Api;

internal static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var catalog = api.MapGroup("/catalog").WithTags("Catalog").RequireRateLimiting(RateLimitPolicies.Catalog);

        catalog.MapGet("/search", async Task<Ok<CatalogSearchResult>> (
                string q,
                CatalogSearchField? field,
                int? page,
                int? pageSize,
                string? language,
                CatalogProvider? provider,
                CatalogService service,
                CancellationToken ct) =>
            TypedResults.Ok(await service.SearchAsync(
                new CatalogSearchRequest(q, field ?? CatalogSearchField.Keyword, page ?? 1, pageSize ?? 20, language, provider), ct)))
            .WithSummary("Search the external book catalogues (Google Books, falling back to Open Library).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        catalog.MapGet("/{provider}/{externalId}", async Task<Ok<CatalogBook>> (CatalogProvider provider, string externalId, CatalogService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetBookAsync(provider, externalId, ct)))
            .WithSummary("Get a catalogue record from a provider.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        catalog.MapPost("/{provider}/{externalId}/import", async Task<Ok<ImportedBookResponse>> (CatalogProvider provider, string externalId, BookService books, CancellationToken ct) =>
                TypedResults.Ok(new ImportedBookResponse(await books.ImportFromCatalogAsync(provider, externalId, ct))))
            .RequireAuthorization(AuthPolicies.ApiUser)
            .WithSummary("Save a catalogue record as a local book (deduplicated by provider id and ISBN).");

        var books = api.MapGroup("/books").WithTags("Books");

        books.MapGet("/{bookId:guid}", async Task<Ok<BookDetailsDto>> (Guid bookId, ClaimsPrincipal user, BookService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetBookAsync(bookId, user.TryGetUserId(), ct)))
            .WithSummary("Get a locally stored book.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        books.MapGet("/{bookId:guid}/related", async Task<Ok<IReadOnlyList<BookSummaryDto>>> (Guid bookId, ClaimsPrincipal user, BookService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetRelatedBooksAsync(bookId, user.TryGetUserId(), 8, ct)))
            .WithSummary("Books sharing an author or genre.");

        books.MapGet("/{bookId:guid}/reviews", async Task<Ok<PagedResult<PublicReviewDto>>> (Guid bookId, int? page, ReviewService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetPublicReviewsForBookAsync(bookId, page ?? 1, 10, ct)))
            .WithSummary("Published reviews for a book.");

        api.MapGet("/reviews/public", async Task<Ok<PagedResult<PublicReviewDto>>> (int? page, ReviewService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetRecentPublicReviewsAsync(page ?? 1, 20, null, ct)))
            .WithTags("Reviews")
            .WithSummary("Most recent published reviews.");
    }
}

public sealed record ImportedBookResponse(Guid BookId);
