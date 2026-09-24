using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Microsoft.Extensions.Logging;
using Square;
using Square.Catalog;
using Square.Core;
using System.Globalization;

namespace Barkfield.Administration.Infrastructure.Services.Square;

/// <summary>
/// Reads Barkfield Road's Square catalog through the Square .NET SDK (v47, the generated client).
/// </summary>
/// <remarks>
/// <para>
/// Square models a product as two objects. The <c>ITEM</c> holds the name and description;
/// each <c>ITEM_VARIATION</c> under it holds a price and SKU. Only the variation is
/// sellable, so everything here returns variations — with the parent item's name attached,
/// because a variation called "Wild-Caught Salmon 7 lb" is unidentifiable on its own.
/// </para>
/// <para>
/// Money crosses the boundary here. Square counts in minor units (3600 is $36.00); the rest
/// of the application works in dollars, and converting anywhere else would eventually mean
/// billing someone a hundred times the right amount.
/// </para>
/// </remarks>
public class SquareCatalogService : ISquareCatalogService
{
    /// <summary>Square caps SearchCatalogItems at 100 results per page.</summary>
    private const int MaxSearchLimit = 100;

    /// <summary>
    /// Ids per BatchRetrieveCatalogObjects call. Square allows up to 1,000; a smaller batch
    /// keeps any single request cheap to retry, and the whole catalog is a few hundred rows.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>A ceiling on the discount listing, in case the pager never stops.</summary>
    private const int MaxDiscounts = 500;

    private readonly ISquareClient _squareClient;
    private readonly ILogger<SquareCatalogService> _logger;

    public SquareCatalogService(ISquareClient squareClient, ILogger<SquareCatalogService> logger)
    {
        _squareClient = squareClient;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SquareVariationDto>> SearchVariationsAsync(
        string searchText,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText, nameof(searchText));

        var request = new SearchCatalogItemsRequest
        {
            TextFilter = searchText.Trim(),
            Limit = Math.Clamp(limit, 1, MaxSearchLimit)
        };

        SearchCatalogItemsResponse response = await SendAsync(
            () => _squareClient.Catalog.SearchItemsAsync(request, cancellationToken: cancellationToken),
            "SearchCatalogItems");

        ThrowIfErrors(response.Errors, "SearchCatalogItems");

        var results = new List<SquareVariationDto>();

        foreach (CatalogObject catalogObject in response.Items ?? [])
        {
            if (!catalogObject.TryAsItem(out CatalogObjectItem? item) || item.ItemData is null) continue;

            // Square returns the matching items, and the caller wants every size and protein
            // under each one — matching "OC Raw" should offer all six variations, not the one
            // whose own name happens to contain the words.
            foreach (CatalogObject variationObject in item.ItemData.Variations ?? [])
            {
                SquareVariationDto? variation = MapVariation(variationObject, item.ItemData.Name, item.Id);

                if (variation is not null) results.Add(variation);
            }
        }

        return results;
    }

    public async Task<SquareVariationDto?> GetVariationAsync(
        string variationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variationId, nameof(variationId));

        // Fully qualified: this file's own namespace ends in "Square", which otherwise wins
        // the lookup against the SDK's.
        var request = new global::Square.Catalog.Object.GetObjectRequest
        {
            ObjectId = variationId.Trim(),

            // Brings back the parent ITEM alongside the variation, so the name can be
            // composed without a second call.
            IncludeRelatedObjects = true
        };

        GetCatalogObjectResponse? response;

        try
        {
            response = await SendAsync(
                () => _squareClient.Catalog.Object.GetAsync(request, cancellationToken: cancellationToken),
                "GetCatalogObject");
        }
        catch (SquareApiException ex) when (ex.StatusCode == 404)
        {
            // A product removed from Square is an ordinary state, not a fault. The caller
            // decides what it means — on import it is an error, on sync it is a report.
            return null;
        }

        ThrowIfErrors(response.Errors, "GetCatalogObject");

        if (response.Object is null) return null;

        Dictionary<string, string> itemNames = IndexItemNames(response.RelatedObjects);

        return MapVariation(response.Object, itemNames);
    }

    public async Task<IReadOnlyCollection<SquareVariationDto>> GetVariationsAsync(
        IEnumerable<string> variationIds,
        CancellationToken cancellationToken = default)
    {
        var ids = variationIds?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (ids.Count == 0) return [];

        var results = new List<SquareVariationDto>(ids.Count);

        foreach (string[] batch in ids.Chunk(BatchSize))
        {
            var request = new BatchGetCatalogObjectsRequest
            {
                ObjectIds = batch,
                IncludeRelatedObjects = true
            };

            BatchGetCatalogObjectsResponse response = await SendAsync(
                () => _squareClient.Catalog.BatchGetAsync(request, cancellationToken: cancellationToken),
                "BatchRetrieveCatalogObjects");

            ThrowIfErrors(response.Errors, "BatchRetrieveCatalogObjects");

            // Ids Square no longer recognises are simply absent from Objects. That is not an
            // error here — the caller reports them rather than deleting rows that delivery
            // history still points at.
            Dictionary<string, string> itemNames = IndexItemNames(response.RelatedObjects);

            foreach (CatalogObject catalogObject in response.Objects ?? [])
            {
                SquareVariationDto? variation = MapVariation(catalogObject, itemNames);

                if (variation is not null) results.Add(variation);
            }
        }

        return results;
    }

    public async Task<IReadOnlyCollection<SquareDiscountDto>> ListDiscountsAsync(
        CancellationToken cancellationToken = default)
    {
        var discounts = new List<SquareDiscountDto>();

        var request = new ListCatalogRequest { Types = "DISCOUNT" };

        try
        {
            // ListAsync returns a pager that follows Square's cursor itself, so enumerating
            // it walks every page. The cap is a guard against a runaway response, not an
            // expectation — a store has a handful of discounts, not thousands.
            Pager<CatalogObject> pager =
                await _squareClient.Catalog.ListAsync(request, cancellationToken: cancellationToken);

            await foreach (CatalogObject catalogObject in pager.WithCancellation(cancellationToken))
            {
                if (!catalogObject.TryAsDiscount(out CatalogObjectDiscount? discount)) continue;
                if (discount.IsDeleted == true || discount.DiscountData is null) continue;

                discounts.Add(new SquareDiscountDto(
                    Id: discount.Id ?? string.Empty,
                    Name: discount.DiscountData.Name ?? string.Empty,
                    DiscountType: discount.DiscountData.DiscountType?.ToString(),
                    Percentage: ParsePercentage(discount.DiscountData.Percentage),
                    Amount: ToDollars(discount.DiscountData.AmountMoney?.Amount)));

                if (discounts.Count >= MaxDiscounts) break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square ListCatalog failed while reading discounts.");

            throw new ExternalServiceException("Square ListCatalog failed.", ex);
        }

        return discounts.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Maps a catalog object to a variation, looking the parent item's name up by its id.
    /// </summary>
    private SquareVariationDto? MapVariation(CatalogObject catalogObject, IReadOnlyDictionary<string, string> itemNames)
    {
        if (!catalogObject.TryAsItemVariation(out CatalogObjectItemVariation? wrapper)) return null;

        string? itemId = wrapper.ItemVariationData?.ItemId;
        string itemName = itemId is not null && itemNames.TryGetValue(itemId, out string? name)
            ? name
            : string.Empty;

        return MapVariation(catalogObject, itemName, itemId);
    }

    private SquareVariationDto? MapVariation(CatalogObject catalogObject, string? itemName, string? itemId)
    {
        if (!catalogObject.TryAsItemVariation(out CatalogObjectItemVariation? wrapper)) return null;

        CatalogItemVariation? data = wrapper.ItemVariationData;

        if (data is null || string.IsNullOrWhiteSpace(wrapper.Id))
        {
            _logger.LogWarning("Skipped a Square catalog variation with no id or variation data.");

            return null;
        }

        long? amount = data.PriceMoney?.Amount;

        // Either flag means there is nothing to bill against: VARIABLE_PRICING is Square's
        // explicit "keyed in at the till", and a missing amount amounts to the same thing.
        bool isVariablePricing =
            string.Equals(data.PricingType?.ToString(), CatalogPricingType.VariablePricing.ToString(), StringComparison.Ordinal)
            || amount is null;

        return new SquareVariationDto(
            VariationId: wrapper.Id,
            ItemId: data.ItemId ?? itemId,
            ItemName: itemName ?? string.Empty,
            VariationName: data.Name ?? string.Empty,
            Sku: data.Sku,
            Price: isVariablePricing ? null : ToDollars(amount),
            IsVariablePricing: isVariablePricing);
    }

    /// <summary>Indexes the ITEM objects Square returns alongside a variation, by item id.</summary>
    private static Dictionary<string, string> IndexItemNames(IEnumerable<CatalogObject>? relatedObjects)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (CatalogObject related in relatedObjects ?? [])
        {
            if (!related.TryAsItem(out CatalogObjectItem? item)) continue;
            if (string.IsNullOrWhiteSpace(item.Id) || item.ItemData?.Name is null) continue;

            names[item.Id] = item.ItemData.Name;
        }

        return names;
    }

    /// <summary>
    /// Converts Square's minor units to dollars. 3600 becomes 36.00.
    /// </summary>
    private static decimal? ToDollars(long? minorUnits) =>
        minorUnits is null ? null : minorUnits.Value / 100m;

    /// <summary>
    /// Square sends a discount percentage as a string, e.g. "12.0". Parsed invariantly —
    /// it is a wire value, not something formatted for the current locale.
    /// </summary>
    private static decimal? ParsePercentage(string? percentage) =>
        decimal.TryParse(percentage, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;

    /// <summary>
    /// Runs a Square call, translating transport failures into <see cref="ExternalServiceException"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SquareApiException"/> is rethrown untouched so callers can inspect the
    /// status code — a 404 from GetCatalogObject means "no such product", which is not a
    /// failure of the integration.
    /// </remarks>
    private async Task<T> SendAsync<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (SquareApiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square {Operation} failed.", operation);

            throw new ExternalServiceException($"Square {operation} failed.", ex);
        }
    }

    /// <summary>
    /// Square answers 200 with an Errors collection for partial failures, so a successful
    /// status code is not on its own a successful call.
    /// </summary>
    private void ThrowIfErrors(IEnumerable<Error>? errors, string operation)
    {
        if (errors is null || !errors.Any()) return;

        string detail = string.Join("; ", errors.Select(e => $"{e.Code}: {e.Detail}"));

        _logger.LogError("Square {Operation} returned errors: {Errors}", operation, detail);

        throw new ExternalServiceException(
            $"Square {operation} returned errors: {detail}",
            new InvalidOperationException(detail));
    }
}
