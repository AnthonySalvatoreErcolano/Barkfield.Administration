using Barkfield.Administration.Application.Services.Sqaure.Dtos;

namespace Barkfield.Administration.Application.Services.Sqaure;

/// <summary>
/// Read access to the Square catalog: item variations and discounts.
/// </summary>
/// <remarks>
/// Separate from <see cref="ISquareService"/>, which owns customer profiles. The catalog is
/// read-only from our side — Barkfield Road's products are maintained in Square, and this
/// application mirrors a subset of them so subscriptions have something stable to point at.
/// </remarks>
public interface ISquareCatalogService
{
    /// <summary>
    /// Searches Square's catalog by text and returns every variation of each matching item.
    /// </summary>
    /// <remarks>
    /// The match is on the item, and all of its variations come back. That is what staff
    /// expect: typing "OC Raw" should offer all six sizes and proteins, not only the one
    /// whose variation name happens to contain the words.
    ///
    /// <paramref name="limit"/> therefore caps matching items, not returned variations.
    /// </remarks>
    Task<IReadOnlyCollection<SquareVariationDto>> SearchVariationsAsync(
        string searchText,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one variation. Returns null if Square does not have it.</summary>
    Task<SquareVariationDto?> GetVariationAsync(string variationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves many variations at once, for re-syncing the local catalog.
    /// </summary>
    /// <remarks>
    /// Ids Square no longer recognises are simply absent from the result — the caller decides
    /// what a missing variation means, because a deleted product is not an error.
    /// </remarks>
    Task<IReadOnlyCollection<SquareVariationDto>> GetVariationsAsync(
        IEnumerable<string> variationIds,
        CancellationToken cancellationToken = default);

    /// <summary>Every discount defined in Square.</summary>
    Task<IReadOnlyCollection<SquareDiscountDto>> ListDiscountsAsync(CancellationToken cancellationToken = default);
}
