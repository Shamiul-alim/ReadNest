using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using ReadNest.Application.Common;
using ReadNest.Application.Notes;
using ReadNest.Application.Reading;
using ReadNest.Domain.Enums;
using ReadNest.Web.Authorization;

namespace ReadNest.Web.Api;

internal static class ReadingEndpoints
{
    public static void MapReadingEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup(string.Empty).WithTags("Reading sessions");

        sessions.MapPost("/library/{entryId:guid}/sessions", async Task<Created<ReadingSessionDto>> (Guid entryId, SessionRequest request, ClaimsPrincipal user, ReadingSessionService service, CancellationToken ct) =>
            {
                var session = await service.LogAsync(user.GetRequiredUserId(), entryId, request, ct);
                return TypedResults.Created($"/api/v1/sessions/{session.Id}", session);
            })
            .WithSummary("Record a reading session. Pages read are derived from startPage and endPage.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapGet("/sessions", async Task<Ok<PagedResult<ReadingSessionDto>>> (DateOnly? from, DateOnly? to, Guid? entryId, int? page, int? pageSize, ClaimsPrincipal user, ReadingSessionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetHistoryAsync(user.GetRequiredUserId(), new HistoryQuery(from, to, entryId, page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Reading history, newest first.");

        sessions.MapPut("/sessions/{sessionId:guid}", async Task<Ok<ReadingSessionDto>> (Guid sessionId, SessionRequest request, ClaimsPrincipal user, ReadingSessionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(user.GetRequiredUserId(), sessionId, request, ct)))
            .WithSummary("Edit a reading session.");

        sessions.MapDelete("/sessions/{sessionId:guid}", async Task<NoContent> (Guid sessionId, ClaimsPrincipal user, ReadingSessionService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(user.GetRequiredUserId(), sessionId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete a reading session.");

        var notes = api.MapGroup(string.Empty).WithTags("Notes");

        notes.MapGet("/notes", async Task<Ok<PagedResult<NoteDto>>> (string? search, NoteType? type, Guid? entryId, int? page, int? pageSize, ClaimsPrincipal user, NoteService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SearchAsync(user.GetRequiredUserId(), new NoteQuery(search, type, entryId, page ?? 1, pageSize ?? 20), ct)))
            .WithSummary("Search your private notes and quotes.");

        notes.MapPost("/library/{entryId:guid}/notes", async Task<Created<NoteDto>> (Guid entryId, NoteRequest request, ClaimsPrincipal user, NoteService service, CancellationToken ct) =>
            {
                var note = await service.CreateAsync(user.GetRequiredUserId(), entryId, request, ct);
                return TypedResults.Created($"/api/v1/notes/{note.Id}", note);
            })
            .WithSummary("Add a note, quote, takeaway, lesson or action item to a library entry.");

        notes.MapPut("/notes/{noteId:guid}", async Task<Ok<NoteDto>> (Guid noteId, NoteRequest request, ClaimsPrincipal user, NoteService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(user.GetRequiredUserId(), noteId, request, ct)))
            .WithSummary("Edit a note.");

        notes.MapDelete("/notes/{noteId:guid}", async Task<NoContent> (Guid noteId, ClaimsPrincipal user, NoteService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(user.GetRequiredUserId(), noteId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete a note.");
    }
}
