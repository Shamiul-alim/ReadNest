using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Books;

/// <summary>
/// Searches the external catalogues. Providers are tried in configured order; a provider that fails or
/// returns nothing for the first page falls through to the next one. Results are cached in memory.
/// </summary>
public sealed class CatalogService(
    IEnumerable<IBookCatalogProvider> providers,
    IMemoryCache cache,
    IOptions<CatalogOptions> options,
    ILogger<CatalogService> logger)
{
    public const int MaxPageSize = 40;
    public const int MaxPage = 25;

    /// <summary>How long a provider that answered "429 Too Many Requests" is skipped.</summary>
    public static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyList<IBookCatalogProvider> _providers = OrderProviders(providers, options.Value.ProviderOrder);

    public IReadOnlyList<CatalogProvider> EnabledProviders => _providers.Select(p => p.Provider).ToList();

    public async Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        var candidates = normalized.Provider is { } only
            ? _providers.Where(p => p.Provider == only).ToList()
            : _providers.ToList();

        if (candidates.Count == 0)
        {
            throw new ServiceUnavailableException("No book catalogue provider is enabled. Books already in your library are still available.");
        }

        var notices = new List<string>();
        CatalogSearchResult? emptyResult = null;
        foreach (var provider in candidates)
        {
            // Skip a provider that recently rate-limited us, unless it is the only option.
            if (candidates.Count > 1 && cache.TryGetValue(CooldownKey(provider.Provider), out _))
            {
                notices.Add($"{provider.Provider.ToDisplayName()} is temporarily unavailable (rate limited).");
                continue;
            }

            try
            {
                var page = await SearchWithCacheAsync(provider, normalized, cancellationToken);
                var items = Deduplicate(page.Items);
                var result = new CatalogSearchResult(items, normalized.Page, normalized.PageSize, page.TotalItems, provider.Provider, notices);
                if (items.Count > 0 || normalized.Page > 1)
                {
                    return result;
                }

                emptyResult ??= result;
                notices.Add($"{provider.Provider.ToDisplayName()} returned no matches.");
            }
            catch (CatalogProviderException ex)
            {
                logger.LogWarning(ex, "Catalogue provider {Provider} failed for a {Field} search", provider.Provider, normalized.Field);
                if (ex.IsRateLimited)
                {
                    cache.Set(CooldownKey(provider.Provider), true, RateLimitCooldown);
                }

                notices.Add(ex.IsRateLimited
                    ? $"{provider.Provider.ToDisplayName()} is rate limiting requests right now."
                    : $"{provider.Provider.ToDisplayName()} is unavailable right now.");
            }
        }

        if (emptyResult is not null)
        {
            return emptyResult with { Notices = notices };
        }

        throw new ServiceUnavailableException(
            "The book catalogue is unavailable right now. Please try again in a moment — books already in your library are still available.");
    }

    public async Task<CatalogBook> GetBookAsync(CatalogProvider providerId, string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > Domain.Entities.BookExternalId.ExternalIdMaxLength)
        {
            throw new NotFoundException("That book could not be found in the catalogue.");
        }

        var provider = _providers.FirstOrDefault(p => p.Provider == providerId)
            ?? throw new ServiceUnavailableException($"{providerId.ToDisplayName()} is not enabled.");

        var key = $"catalog:book:{providerId}:{externalId}";
        if (cache.TryGetValue(key, out CatalogBook? cached) && cached is not null)
        {
            return cached;
        }

        CatalogBook? book;
        try
        {
            book = await provider.GetByIdAsync(externalId, cancellationToken);
        }
        catch (CatalogProviderException ex)
        {
            logger.LogWarning(ex, "Catalogue provider {Provider} failed to load a book", providerId);
            throw new ServiceUnavailableException(
                $"{providerId.ToDisplayName()} is unavailable right now, so this book cannot be loaded. Please try again shortly.");
        }

        if (book is null)
        {
            throw new NotFoundException("That book could not be found in the catalogue.");
        }

        cache.Set(key, book, TimeSpan.FromMinutes(options.Value.BookCacheMinutes));
        return book;
    }

    internal static CatalogSearchRequest Normalize(CatalogSearchRequest request)
    {
        var query = InputSanitizer.SearchTerm(request.Query, "query")
            ?? throw new RequestValidationException("query", "Enter a title, author, ISBN or keyword to search for.");

        if (!Enum.IsDefined(request.Field))
        {
            throw new RequestValidationException("field", "Unknown search field.");
        }

        if (request.Field == CatalogSearchField.Isbn)
        {
            query = Isbn.NormalizeToIsbn13(query)
                ?? throw new RequestValidationException("query", "Enter a valid ISBN-10 or ISBN-13.");
        }

        var language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language.Trim().ToLowerInvariant();
        if (language is not null && (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetterLower)))
        {
            language = null;
        }

        return request with
        {
            Query = query,
            Page = Math.Clamp(request.Page, 1, MaxPage),
            PageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, MaxPageSize),
            Language = language,
        };
    }

    /// <summary>Removes duplicate editions within one page (same ISBN-13, or same title and first author).</summary>
    internal static IReadOnlyList<CatalogBook> Deduplicate(IReadOnlyList<CatalogBook> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CatalogBook>(items.Count);
        foreach (var item in items)
        {
            var key = item.Isbn13 is { } isbn
                ? "isbn:" + isbn
                : "ta:" + TextNormalizer.Key(item.Title) + "|" + TextNormalizer.Key(item.Authors.Count > 0 ? item.Authors[0] : string.Empty);
            if (seen.Add(key))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private async Task<CatalogProviderPage> SearchWithCacheAsync(IBookCatalogProvider provider, CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"catalog:search:{provider.Provider}:{request.Field}:{request.Language}:{request.Page}:{request.PageSize}:{request.Query.ToUpperInvariant()}");
        if (cache.TryGetValue(key, out CatalogProviderPage? cached) && cached is not null)
        {
            return cached;
        }

        var page = await provider.SearchAsync(request, cancellationToken);
        cache.Set(key, page, TimeSpan.FromMinutes(options.Value.SearchCacheMinutes));
        return page;
    }

    private static string CooldownKey(CatalogProvider provider) => $"catalog:cooldown:{provider}";

    private static List<IBookCatalogProvider> OrderProviders(IEnumerable<IBookCatalogProvider> providers, IReadOnlyList<CatalogProvider> order)
    {
        var rank = new Dictionary<CatalogProvider, int>();
        for (var i = 0; i < order.Count; i++)
        {
            rank.TryAdd(order[i], i);
        }

        return providers
            .Where(p => p.IsEnabled)
            .OrderBy(p => rank.GetValueOrDefault(p.Provider, int.MaxValue))
            .ToList();
    }
}
