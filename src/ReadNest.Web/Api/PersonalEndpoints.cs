using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using ReadNest.Application.Analytics;
using ReadNest.Application.Common;
using ReadNest.Application.Goals;
using ReadNest.Application.ImportExport;
using ReadNest.Application.Library;
using ReadNest.Application.Recommendations;
using ReadNest.Application.Reviews;
using ReadNest.Web.Authorization;
using ReadNest.Web.Configuration;

namespace ReadNest.Web.Api;

internal static class PersonalEndpoints
{
    public static void MapPersonalEndpoints(this RouteGroupBuilder api)
    {
        var stats = api.MapGroup(string.Empty).WithTags("Dashboard and goals");

        stats.MapGet("/dashboard", async Task<Ok<DashboardDto>> (ClaimsPrincipal user, StatisticsService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetDashboardAsync(user.GetRequiredUserId(), ct)))
            .WithSummary("Dashboard statistics calculated from your library and reading sessions.");

        stats.MapGet("/analytics/{year:int}", async Task<Ok<YearAnalyticsDto>> (int year, ClaimsPrincipal user, StatisticsService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetYearAnalyticsAsync(user.GetRequiredUserId(), year, ct)))
            .WithSummary("Monthly and yearly reading statistics.");

        stats.MapGet("/goals", async Task<Ok<IReadOnlyList<GoalSummaryDto>>> (ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(user.GetRequiredUserId(), ct)))
            .WithSummary("All yearly goals.");

        stats.MapGet("/goals/{year:int}", async Task<Ok<GoalProgressDto>> (int year, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetProgressAsync(user.GetRequiredUserId(), year, ct)))
            .WithSummary("Progress toward a year's goal (hasGoal is false when no goal is set).");

        stats.MapPut("/goals/{year:int}", async Task<Ok<GoalProgressDto>> (int year, GoalRequest request, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetGoalAsync(user.GetRequiredUserId(), year, request, ct)))
            .WithSummary("Create or update the goal for a year.");

        stats.MapDelete("/goals/{year:int}", async Task<NoContent> (int year, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            {
                await service.DeleteGoalAsync(user.GetRequiredUserId(), year, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete a year's goal.");

        var reviews = api.MapGroup(string.Empty).WithTags("Reviews");

        reviews.MapPut("/library/{entryId:guid}/review", async Task<NoContent> (Guid entryId, ReviewRequest request, ClaimsPrincipal user, ReviewService service, CancellationToken ct) =>
            {
                await service.SaveReviewAsync(user.GetRequiredUserId(), entryId, request, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Create or update your rating and review for a library entry.");

        reviews.MapDelete("/library/{entryId:guid}/review", async Task<NoContent> (Guid entryId, ClaimsPrincipal user, ReviewService service, CancellationToken ct) =>
            {
                await service.DeleteReviewAsync(user.GetRequiredUserId(), entryId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete your written review (the star rating is kept).");

        reviews.MapGet("/reviews/mine", async Task<Ok<PagedResult<MyReviewDto>>> (int? page, ClaimsPrincipal user, ReviewService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetMyReviewsAsync(user.GetRequiredUserId(), page ?? 1, 20, ct)))
            .WithSummary("Your ratings and reviews, including private ones.");

        var shelves = api.MapGroup("/shelves").WithTags("Shelves");

        shelves.MapGet("/", async Task<Ok<IReadOnlyList<ShelfDto>>> (ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(user.GetRequiredUserId(), ct)));

        shelves.MapPost("/", async Task<Created<ShelfDto>> (ShelfRequest request, ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
        {
            var userId = user.GetRequiredUserId();
            var id = await service.CreateAsync(userId, request, ct);
            return TypedResults.Created($"/api/v1/shelves/{id}", await service.GetAsync(userId, id, ct));
        });

        shelves.MapPut("/{shelfId:guid}", async Task<NoContent> (Guid shelfId, ShelfRequest request, ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(user.GetRequiredUserId(), shelfId, request, ct);
            return TypedResults.NoContent();
        });

        shelves.MapDelete("/{shelfId:guid}", async Task<NoContent> (Guid shelfId, ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(user.GetRequiredUserId(), shelfId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Delete a shelf. Books stay in your library.");

        shelves.MapPut("/{shelfId:guid}/entries/{entryId:guid}", async Task<NoContent> (Guid shelfId, Guid entryId, ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
        {
            await service.AddEntryAsync(user.GetRequiredUserId(), shelfId, entryId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Put a library entry on a shelf (idempotent).");

        shelves.MapDelete("/{shelfId:guid}/entries/{entryId:guid}", async Task<NoContent> (Guid shelfId, Guid entryId, ClaimsPrincipal user, ShelfService service, CancellationToken ct) =>
        {
            await service.RemoveEntryAsync(user.GetRequiredUserId(), shelfId, entryId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Take a library entry off a shelf.");

        api.MapGet("/recommendations", async Task<Ok<RecommendationsDto>> (ClaimsPrincipal user, RecommendationService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetRecommendationsAsync(user.GetRequiredUserId(), 12, ct)))
            .WithTags("Recommendations")
            .WithSummary("Rule-based recommendations with an explanation for each book.");

        var data = api.MapGroup(string.Empty).WithTags("Import and export");

        data.MapGet("/export/library.csv", async Task<FileContentHttpResult> (ClaimsPrincipal user, ImportExportService service, CancellationToken ct) =>
            {
                var csv = await service.ExportCsvAsync(user.GetRequiredUserId(), ct);
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return TypedResults.File(bytes, "text/csv; charset=utf-8", $"readnest-library-{Stamp()}.csv");
            })
            .WithSummary("Download your library as CSV (formula-injection safe).");

        data.MapGet("/export/library.json", async Task<FileContentHttpResult> (ClaimsPrincipal user, ImportExportService service, CancellationToken ct) =>
                TypedResults.File(await service.ExportJsonAsync(user.GetRequiredUserId(), ct), "application/json", $"readnest-export-{Stamp()}.json"))
            .WithSummary("Download all your reading data as JSON.");

        data.MapPost("/import/library.csv", async Task<Ok<ImportSummary>> (IFormFile file, ClaimsPrincipal user, ImportExportService service, CancellationToken ct) =>
            {
                if (file.Length == 0 || file.Length > ImportExportService.MaxImportBytes)
                {
                    throw new RequestValidationException("file", $"Upload a CSV file between 1 byte and {ImportExportService.MaxImportBytes / 1024} KB.");
                }

                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await service.ImportCsvAsync(user.GetRequiredUserId(), stream, ct));
            })
            .DisableAntiforgery()
            .RequireAuthorization(AuthPolicies.BearerOnly)
            .RequireRateLimiting(RateLimitPolicies.Import)
            .WithSummary("Import a library CSV (multipart field 'file'). Requires a bearer token.");
    }

    private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
