using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using ReadNest.Application.Common;
using ReadNest.Application.Reading;
using ReadNest.Web.Authorization;
using ReadNest.Web.Configuration;

namespace ReadNest.Web.Api;

public sealed record GutenbergImportRequest(int GutenbergId);

internal static class ReaderEndpoints
{
    public static void MapReaderEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/library/{entryId:guid}/reading-file").WithTags("Reading files");
        var limits = UploadLimits.From(api);

        files.MapGet("/", async Task<Results<Ok<ReadingFileDto>, NotFound>> (Guid entryId, ClaimsPrincipal user, ReadingFileService service, CancellationToken ct) =>
                await service.GetAsync(user.GetRequiredUserId(), entryId, ct) is { } dto ? TypedResults.Ok(dto) : TypedResults.NotFound())
            .WithSummary("The PDF or EPUB attached to a library entry, with your saved position. 404 when none is attached.");

        files.MapPost("/", async Task<Ok<ReadingFileDto>> (Guid entryId, IFormFile file, ClaimsPrincipal user, ReadingFileService service, CancellationToken ct) =>
            {
                if (file.Length == 0 || file.Length > service.MaxBytes)
                {
                    throw new RequestValidationException("file", $"Upload a PDF or EPUB file up to {Application.Abstractions.UploadOptions.Describe(service.MaxBytes)}.");
                }

                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await service.UploadAsync(user.GetRequiredUserId(), entryId, stream, file.FileName, ct));
            })
            .DisableAntiforgery()
            .RequireAuthorization(AuthPolicies.BearerOnly)
            .RequireRateLimiting(RateLimitPolicies.Import)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(limits.ReadingFileRequestBytes))
            .WithSummary("Attach or replace the entry's reading file (multipart field 'file'; .pdf or .epub). Requires a bearer token.")
            .WithDescription("The format is detected from the content and must match the extension. A replaced file is deleted only after the new one is stored. The saved position is reset.");

        files.MapDelete("/", async Task<NoContent> (Guid entryId, ClaimsPrincipal user, ReadingFileService service, CancellationToken ct) =>
            {
                await service.RemoveAsync(user.GetRequiredUserId(), entryId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Remove the entry's reading file. Your reading progress and sessions are kept.");

        files.MapGet("/content", async Task<FileStreamHttpResult> (Guid entryId, bool? download, ClaimsPrincipal user, HttpContext http, ReadingFileService service, CancellationToken ct) =>
            {
                var file = await service.OpenAsync(user.GetRequiredUserId(), entryId, ct);

                // Private, never cached by shared caches, and inert if opened directly: the sandbox CSP stops any
                // script inside the file from running with ReadNest's origin.
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return TypedResults.Stream(
                    file.Content,
                    ReadingFileInspector.ContentTypeFor(file.Format),
                    // The reader fetches the bytes itself; only an explicit download asks the browser to save a file.
                    fileDownloadName: download == true ? file.FileName : null,
                    lastModified: new DateTimeOffset(file.UploadedAtUtc, TimeSpan.Zero),
                    entityTag: new EntityTagHeaderValue("\"" + file.Sha256 + "\""),
                    enableRangeProcessing: true);
            })
            .WithSummary("The entry's reading file (owner only; supports range requests). Add download=true to save it as a file.");

        files.MapPut("/position", async Task<Ok<ReaderPositionDto>> (Guid entryId, ReaderPositionRequest request, ClaimsPrincipal user, ReadingFileService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SavePositionAsync(user.GetRequiredUserId(), entryId, request, ct)))
            .Accepts<ReaderPositionRequest>(ApiEndpoints.Json)
            .WithSummary("Save your place in the file: an EPUB CFI or PDF page in 'location', plus percent (0-100) and, for PDFs, page and pageCount.")
            .WithDescription("This is a bookmark only. It does not record a reading session; use PUT /library/{entryId}/progress for that.");

        files.MapPost("/gutenberg", async Task<Ok<ReadingFileDto>> (Guid entryId, GutenbergImportRequest request, ClaimsPrincipal user, ReadingSourcesService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ImportGutenbergAsync(user.GetRequiredUserId(), entryId, request.GutenbergId, ct)))
            .Accepts<GutenbergImportRequest>(ApiEndpoints.Json)
            .RequireRateLimiting(RateLimitPolicies.Import)
            .WithSummary("Attach a public-domain EPUB from Project Gutenberg to the entry.")
            .WithDescription("Refused unless Project Gutenberg marks the book as not under copyright (public domain in the USA). Check your own country's copyright law.");

        api.MapGet("/books/{bookId:guid}/reading-sources", async Task<Ok<IReadOnlyList<ReadingSourceResult>>> (Guid bookId, ClaimsPrincipal user, ReadingSourcesService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAllAsync(user.GetRequiredUserId(), bookId, ct)))
            .WithTags("Reading files")
            .WithSummary("Ways to read a book: your own file, Google Books full view or preview, Open Library, Project Gutenberg.")
            .WithDescription("Only options the providers actually report are returned. Previews are partial; Open Library borrowing happens on Open Library. 'unavailable' means the provider could not be reached.");
    }
}
