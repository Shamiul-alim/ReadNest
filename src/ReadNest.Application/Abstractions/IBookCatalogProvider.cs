using ReadNest.Application.Books;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Abstractions;

/// <summary>
/// An external book catalogue (Google Books, Open Library, ...). Implementations normalise provider
/// payloads into <see cref="CatalogBook"/> and throw <see cref="CatalogProviderException"/> on failure.
/// </summary>
public interface IBookCatalogProvider
{
    CatalogProvider Provider { get; }

    bool IsEnabled { get; }

    Task<CatalogProviderPage> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the book, or null when the provider reports that it does not exist.</summary>
    Task<CatalogBook?> GetByIdAsync(string externalId, CancellationToken cancellationToken);
}

public sealed class CatalogProviderException(CatalogProvider provider, string message, Exception? innerException = null, bool isRateLimited = false)
    : Exception(message, innerException)
{
    public CatalogProvider Provider { get; } = provider;

    public bool IsRateLimited { get; } = isRateLimited;
}
