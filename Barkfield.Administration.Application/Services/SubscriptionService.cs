using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Subscriptions.Models;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// Customers' recurring auto-ship orders.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation here follows the same three steps: load the aggregate, call one domain
/// method, save the aggregate. Nothing in this class decides what is legal — <see cref="Subscription"/>
/// owns the invariants, and routing every change through it means a rule cannot be enforced in
/// one code path and forgotten in another.
/// </para>
/// <para>
/// The load happens inside the request, not when the page was rendered, so two staff working on
/// the same subscription do not overwrite each other: a request that raises one item's quantity
/// reads the other person's newly-added item and saves it back untouched. The optimistic check
/// on save covers the remaining case of two requests interleaving mid-flight.
/// </para>
/// </remarks>
public class SubscriptionService
{
    private readonly ISubscriptionQueries _subscriptionQueries;
    private readonly ISubscriptionCommands _subscriptionCommands;
    private readonly ICustomerQueries _customerQueries;
    private readonly IProductQueries _productQueries;

    public SubscriptionService(
        ISubscriptionQueries subscriptionQueries,
        ISubscriptionCommands subscriptionCommands,
        ICustomerQueries customerQueries,
        IProductQueries productQueries)
    {
        _subscriptionQueries = subscriptionQueries;
        _subscriptionCommands = subscriptionCommands;
        _customerQueries = customerQueries;
        _productQueries = productQueries;
    }

    // --- Reads -------------------------------------------------------------

    public Task<PagedResult<SubscriptionListItemDto>> SearchAsync(
        SubscriptionFilter filter,
        CancellationToken cancellationToken = default) =>
        _subscriptionQueries.SearchAsync(filter, cancellationToken);

    public async Task<SubscriptionDetailDto> GetAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        await _subscriptionQueries.GetByIdAsync(subscriptionId, cancellationToken)
            ?? throw new NotFoundException($"Subscription with ID '{subscriptionId}' was not found.");

    public async Task<IReadOnlyCollection<SubscriptionListItemDto>> GetForCustomerAsync(
        Guid customerId,
        bool includeCanceled = false,
        CancellationToken cancellationToken = default)
    {
        _ = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        return await _subscriptionQueries.GetByCustomerIdAsync(customerId, includeCanceled, cancellationToken);
    }

    /// <summary>
    /// Projects the next <paramref name="cycles"/> deliveries without writing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first cycle comes from <see cref="Subscription.BuildNextDeliveryManifest"/> — the same
    /// method that will build the real delivery — so the preview cannot disagree with what
    /// actually gets scheduled. Later cycles walk each rotation forward with
    /// <see cref="RotationGroup.PeekUpcoming"/> and drop add-ons, which expire after one delivery.
    /// </para>
    /// <para>
    /// Nothing is persisted and no cursor moves. A paused or canceled subscription previews as
    /// empty, because that is what it would ship.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyCollection<DeliveryPreviewDto>> PreviewUpcomingAsync(
        Guid subscriptionId,
        int cycles = 1,
        CancellationToken cancellationToken = default)
    {
        if (cycles is < 1 or > 24)
        {
            throw new ValidationException("Preview must cover between 1 and 24 cycles.");
        }

        SubscriptionDetailDto dto = await GetAsync(subscriptionId, cancellationToken);
        Subscription subscription = SubscriptionRehydrator.Rehydrate(dto);

        Dictionary<Guid, SubscriptionLineDto> catalog = BuildCatalogLookup(dto);

        var previews = new List<DeliveryPreviewDto>(cycles);

        // Cycle 1 is the real manifest, add-ons included.
        DeliveryManifest first = subscription.BuildNextDeliveryManifest();

        previews.Add(new DeliveryPreviewDto(
            first.DeliveryDate,
            1,
            first.Lines.Select(line => ToPreviewLine(line, subscription, catalog)).ToList()));

        DateTime date = first.DeliveryDate;

        for (int cycle = 2; cycle <= cycles; cycle++)
        {
            date = subscription.Frequency.CalculateNextDate(date).Date;

            var lines = new List<DeliveryPreviewLineDto>();

            foreach (SubscriptionItem item in subscription.Items.OrderBy(i => i.CreatedAt))
            {
                lines.Add(BuildPreviewLine(
                    item.ProductId, item.Quantity, DeliveryLineSource.Recurring, item.Id, null, catalog));
            }

            foreach (RotationGroup group in subscription.RotationGroups.Where(g => g.IsActive).OrderBy(g => g.CreatedAt))
            {
                // PeekUpcoming wraps, so the (cycle - 1)th step ahead is this cycle's pick.
                IReadOnlyList<RotationGroupItem> upcoming = group.PeekUpcoming(cycle);
                if (upcoming.Count < cycle) continue;

                RotationGroupItem pick = upcoming[cycle - 1];

                lines.Add(BuildPreviewLine(
                    pick.ProductId, pick.Quantity, DeliveryLineSource.Rotation, group.Id, group.Name, catalog));
            }

            // Add-ons are deliberately absent: they ship once and are consumed.
            previews.Add(new DeliveryPreviewDto(date, cycle, lines));
        }

        return previews;
    }

    // --- Create ------------------------------------------------------------

    /// <summary>
    /// Creates a subscription in <see cref="SubscriptionStatus.NewSignUp"/>.
    /// </summary>
    /// <remarks>
    /// A customer may hold several, on different cadences, so nothing here refuses a second one.
    /// The subscription starts empty and must be activated once it has something to ship — the
    /// domain refuses to activate an empty one.
    /// </remarks>
    public async Task<Guid> CreateAsync(
        Guid customerId,
        int frequencyInterval,
        FrequencyUnit frequencyUnit,
        DateTime firstDeliveryDate,
        FulfillmentMethod fulfillmentMethod,
        string? name,
        CancellationToken cancellationToken = default)
    {
        CustomerDetailDto customer = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        if (!customer.IsActive)
        {
            throw new ValidationException($"{customer.FullName} is deactivated and cannot take a new subscription.");
        }

        // A driven route needs somewhere to drive to. Pickup and shipping do not.
        if (fulfillmentMethod == FulfillmentMethod.LocalDelivery && !customer.CanReceiveLocalDelivery)
        {
            throw new ValidationException(
                $"{customer.FullName} has no address on file, so a local delivery subscription cannot be created.");
        }

        Subscription subscription = Subscription.Create(
            customerId,
            OrderFrequency.Every(frequencyInterval, frequencyUnit),
            firstDeliveryDate,
            fulfillmentMethod,
            name);

        await _subscriptionCommands.CreateAsync(subscription, cancellationToken);

        return subscription.Id;
    }

    // --- Cadence and lifecycle ---------------------------------------------

    public Task RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Rename(name), cancellationToken);

    public Task ChangeFrequencyAsync(
        Guid id,
        int interval,
        FrequencyUnit unit,
        bool recalculateNextDelivery,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.ChangeFrequency(OrderFrequency.Every(interval, unit), recalculateNextDelivery), cancellationToken);

    public Task ChangeFulfillmentMethodAsync(
        Guid id,
        FulfillmentMethod method,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.ChangeFulfillmentMethod(method), cancellationToken);

    /// <summary>Moves the next delivery to a specific date. The manual override.</summary>
    public Task RescheduleAsync(Guid id, DateTime newDate, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Reschedule(newDate), cancellationToken);

    /// <summary>Pushes the next delivery out one cycle. Rotations do not advance.</summary>
    public Task SkipNextDeliveryAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.SkipNextDelivery(), cancellationToken);

    public Task ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Activate(), cancellationToken);

    /// <summary><paramref name="resumeOn"/> null pauses open-endedly.</summary>
    public Task PauseAsync(Guid id, DateTime? resumeOn, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Pause(resumeOn), cancellationToken);

    public Task ResumeAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Resume(), cancellationToken);

    /// <summary>Permanent. A customer who returns gets a new subscription.</summary>
    public Task CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.Cancel(), cancellationToken);

    // --- Static items ------------------------------------------------------

    public async Task AddItemAsync(
        Guid id,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductIsUsableAsync(productId, cancellationToken);

        await MutateAsync(id, s => s.AddItem(productId, quantity), cancellationToken);
    }

    public Task ChangeItemQuantityAsync(
        Guid id,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.ChangeItemQuantity(productId, quantity), cancellationToken);

    public Task RemoveItemAsync(Guid id, Guid productId, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.RemoveItem(productId), cancellationToken);

    // --- Rotation groups ---------------------------------------------------

    public async Task<Guid> AddRotationGroupAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        Guid groupId = Guid.Empty;

        await MutateAsync(id, s => groupId = s.AddRotationGroup(name).Id, cancellationToken);

        return groupId;
    }

    public Task RenameRotationGroupAsync(
        Guid id,
        Guid groupId,
        string name,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.GetRotationGroup(groupId).Rename(name), cancellationToken);

    public Task RemoveRotationGroupAsync(Guid id, Guid groupId, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.RemoveRotationGroup(groupId), cancellationToken);

    public Task SetRotationGroupActiveAsync(
        Guid id,
        Guid groupId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s =>
        {
            RotationGroup group = s.GetRotationGroup(groupId);

            if (isActive) group.Resume();
            else group.Pause();
        }, cancellationToken);

    public async Task<Guid> AddRotationItemAsync(
        Guid id,
        Guid groupId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductIsUsableAsync(productId, cancellationToken);

        Guid itemId = Guid.Empty;

        await MutateAsync(id, s => itemId = s.GetRotationGroup(groupId).AddItem(productId, quantity).Id, cancellationToken);

        return itemId;
    }

    public Task ChangeRotationItemQuantityAsync(
        Guid id,
        Guid groupId,
        Guid itemId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.GetRotationGroup(groupId).ChangeItemQuantity(itemId, quantity), cancellationToken);

    public Task RemoveRotationItemAsync(
        Guid id,
        Guid groupId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.GetRotationGroup(groupId).RemoveItem(itemId), cancellationToken);

    /// <summary>Replaces the rotation order. The currently-scheduled product stays scheduled.</summary>
    public Task ReorderRotationAsync(
        Guid id,
        Guid groupId,
        IEnumerable<Guid> itemIdsInOrder,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.GetRotationGroup(groupId).Reorder(itemIdsInOrder), cancellationToken);

    /// <summary>Forces a product to be next out — "give them lamb this time".</summary>
    public Task JumpRotationToAsync(
        Guid id,
        Guid groupId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.GetRotationGroup(groupId).JumpTo(itemId), cancellationToken);

    // --- Add-ons -----------------------------------------------------------

    public async Task<Guid> AddAddOnAsync(
        Guid id,
        Guid productId,
        int quantity,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductIsUsableAsync(productId, cancellationToken);

        Guid addOnId = Guid.Empty;

        await MutateAsync(id, s => addOnId = s.AddAddOn(productId, quantity, note).Id, cancellationToken);

        return addOnId;
    }

    public Task ChangeAddOnQuantityAsync(
        Guid id,
        Guid addOnId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.ChangeAddOnQuantity(addOnId, quantity), cancellationToken);

    public Task RemoveAddOnAsync(Guid id, Guid addOnId, CancellationToken cancellationToken = default) =>
        MutateAsync(id, s => s.RemoveAddOn(addOnId), cancellationToken);

    // --- Internals ---------------------------------------------------------

    /// <summary>
    /// Load, mutate, save. The only write path in this service.
    /// </summary>
    private async Task MutateAsync(
        Guid subscriptionId,
        Action<Subscription> mutate,
        CancellationToken cancellationToken)
    {
        SubscriptionDetailDto dto = await GetAsync(subscriptionId, cancellationToken);

        Subscription subscription = SubscriptionRehydrator.Rehydrate(dto);

        mutate(subscription);

        if (!await _subscriptionCommands.SaveAsync(subscription, dto.Revision, cancellationToken))
        {
            throw new ConflictException(
                "This subscription was changed by someone else while you were saving. Reload and try again.");
        }
    }

    /// <summary>
    /// Confirms a product exists and is still in the catalog before it goes onto a subscription.
    /// </summary>
    /// <remarks>
    /// Products already on a subscription when they are discontinued stay there — the detail
    /// read flags them so staff can deal with them deliberately. This only stops a new one
    /// being added, which would otherwise put a product Square no longer sells into a future box.
    /// </remarks>
    private async Task EnsureProductIsUsableAsync(Guid productId, CancellationToken cancellationToken)
    {
        ProductDto product = await _productQueries.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{productId}' was not found.");

        if (!product.IsActive)
        {
            throw new ValidationException(
                $"'{product.Name}' has been discontinued and cannot be added to a subscription.");
        }
    }

    /// <summary>
    /// Rebuilds the aggregate from the detail read, so the domain owns every subsequent change.
    /// </summary>
    /// <summary>
    /// Product name and price for every product the subscription references, taken from the
    /// detail read rather than re-queried.
    /// </summary>
    private static Dictionary<Guid, SubscriptionLineDto> BuildCatalogLookup(SubscriptionDetailDto dto)
    {
        var lookup = new Dictionary<Guid, SubscriptionLineDto>();

        foreach (SubscriptionLineDto line in dto.Items
            .Concat(dto.RotationGroups.SelectMany(g => g.Items))
            .Concat(dto.PendingAddOns))
        {
            lookup.TryAdd(line.ProductId, line);
        }

        return lookup;
    }

    private static DeliveryPreviewLineDto ToPreviewLine(
        DeliveryLineItem line,
        Subscription subscription,
        Dictionary<Guid, SubscriptionLineDto> catalog)
    {
        // A rotation line's source id is the group, so the group's name is the useful label.
        string? label = line.Source switch
        {
            DeliveryLineSource.Rotation =>
                subscription.RotationGroups.FirstOrDefault(g => g.Id == line.SourceId)?.Name,
            DeliveryLineSource.AddOn =>
                subscription.AddOns.FirstOrDefault(a => a.Id == line.SourceId)?.Note,
            _ => null
        };

        return BuildPreviewLine(line.ProductId, line.Quantity, line.Source, line.SourceId, label, catalog);
    }

    private static DeliveryPreviewLineDto BuildPreviewLine(
        Guid productId,
        int quantity,
        DeliveryLineSource source,
        Guid sourceId,
        string? sourceLabel,
        Dictionary<Guid, SubscriptionLineDto> catalog)
    {
        catalog.TryGetValue(productId, out SubscriptionLineDto? product);

        return new DeliveryPreviewLineDto(
            productId,
            product?.ProductName ?? "(unknown product)",
            quantity,
            product?.UnitPrice ?? 0m,
            source.ToString(),
            sourceId,
            sourceLabel,
            product?.ProductIsActive ?? false);
    }
}
