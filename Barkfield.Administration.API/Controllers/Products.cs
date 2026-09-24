using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Products;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Products.Models;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// The product catalog: a local mirror of the Square item variations that appear on subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// There is no create or update endpoint, and that is deliberate. Products are maintained in
/// Square, where staff already manage them and where the point of sale reads them. A product
/// enters this catalog by being imported, and changes by being synced. Letting the admin API
/// edit a name or price would create two sources of truth for what a customer is charged.
/// </para>
/// <para>
/// Viewing is separated from managing: anyone who can build a subscription needs
/// <c>product:view</c>, while importing, syncing and discontinuing need <c>product:manage</c>.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/products")]
public class ProductsController(IProductQueries productQueries, ProductService productService) : ControllerBase
{
    private readonly IProductQueries _productQueries = productQueries;
    private readonly ProductService _productService = productService;

    /// <summary>
    /// Returns the local catalog — paged, searchable and sortable.
    /// </summary>
    /// <param name="searchTerm">Matched against the display name, item name, variation name and SKU.</param>
    /// <param name="squareItemId">Narrows to one Square item's variations — the sizes of a single product.</param>
    /// <param name="includeInactive">Includes products that have been discontinued.</param>
    /// <param name="sortBy">One of: name, price, sku, createdAt, lastSyncedAt.</param>
    [HttpGet]
    [RequirePermission(Permissions.Products.View)]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? searchTerm,
        [FromQuery] string? squareItemId,
        [FromQuery] bool includeInactive,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProductFilter(
            searchTerm,
            includeInactive,
            squareItemId,
            pageNumber,
            pageSize,
            sortBy,
            sortDescending);

        PagedResult<ProductDto> products = await _productService.SearchAsync(filter, cancellationToken);

        return Ok(products);
    }

    /// <summary>
    /// Returns one product.
    /// </summary>
    [HttpGet("{productId:guid}", Name = nameof(GetProductById))]
    [RequirePermission(Permissions.Products.View)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById([FromRoute] Guid productId, CancellationToken cancellationToken)
    {
        ProductDto? product = await _productQueries.GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = $"Product with ID '{productId}' was not found." });
        }

        return Ok(product);
    }

    /// <summary>
    /// Searches the Square catalog, marking which results are already imported.
    /// </summary>
    /// <remarks>
    /// This is what the product picker calls. It searches Square rather than the local
    /// catalog, because the products staff most often want are the ones not yet imported —
    /// searching locally first would hide exactly those. Each row says whether it is already
    /// in the catalog, whether its local price has fallen behind Square's, and whether it can
    /// be imported at all.
    /// </remarks>
    /// <param name="q">Free text, matched by Square against item names.</param>
    /// <param name="limit">
    /// Matching <i>items</i> to return, 1–100. Every variation of each one comes back, so the
    /// number of rows is usually higher — six sizes under one item is one match, not six.
    /// </param>
    /// <response code="502">Square is unreachable or returned an error.</response>
    [HttpGet("search")]
    [RequirePermission(Permissions.Products.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<CatalogSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SearchSquareCatalog(
        [FromQuery] string q,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CatalogSearchResultDto> results =
            await _productService.SearchSquareCatalogAsync(q, limit, cancellationToken);

        return Ok(results);
    }

    /// <summary>
    /// Imports a Square variation into the local catalog.
    /// </summary>
    /// <remarks>
    /// Idempotent: importing something already here returns the existing product, refreshed
    /// from Square and reactivated if it had been discontinued. Variable-priced products are
    /// rejected — Square holds no price for them, so there would be nothing to bill against.
    /// </remarks>
    /// <response code="400">The variation is priced per sale in Square.</response>
    /// <response code="404">Square has no variation with that id.</response>
    [HttpPost("import")]
    [RequirePermission(Permissions.Products.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ImportProduct(
        [FromBody] ImportProductRequest request,
        CancellationToken cancellationToken)
    {
        Guid productId = await _productService.ImportAsync(request.SquareVariationId, cancellationToken);

        // Deliberately 200, not 201. The call is idempotent, and a repeat import returns the
        // product that was already there — reporting "Created" for it would be a lie.
        return Ok(productId);
    }

    /// <summary>
    /// Refreshes one product's name, price and SKU from Square.
    /// </summary>
    /// <response code="404">The product is gone from this catalog, or from Square.</response>
    [HttpPost("{productId:guid}/sync")]
    [RequirePermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SyncProduct([FromRoute] Guid productId, CancellationToken cancellationToken)
    {
        await _productService.SyncAsync(productId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Refreshes every active product against Square and reports what changed.
    /// </summary>
    /// <remarks>
    /// Meant to be run before a billing day. Products Square no longer has — or that have
    /// been switched to variable pricing — are listed rather than removed, because deleting
    /// them would break the subscription lines and delivery history that reference them.
    /// </remarks>
    [HttpPost("sync")]
    [RequirePermission(Permissions.Products.Manage)]
    [ProducesResponseType(typeof(CatalogSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SyncCatalog(CancellationToken cancellationToken)
    {
        CatalogSyncResult result = await _productService.SyncAllAsync(cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Discontinues a product. A soft delete — it stops appearing in pickers, and every
    /// subscription line and past delivery that references it is left intact.
    /// </summary>
    [HttpDelete("{productId:guid}")]
    [RequirePermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateProduct([FromRoute] Guid productId, CancellationToken cancellationToken)
    {
        await _productService.DeactivateAsync(productId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Restores a discontinued product to the catalog.
    /// </summary>
    [HttpPost("{productId:guid}/reactivate")]
    [RequirePermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateProduct([FromRoute] Guid productId, CancellationToken cancellationToken)
    {
        await _productService.ReactivateAsync(productId, cancellationToken);

        return NoContent();
    }
}
