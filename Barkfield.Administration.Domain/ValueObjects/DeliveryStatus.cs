namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Where a single delivery sits in the packing and dispatch workflow.
/// Replaces the old OrderStatus enum, which mixed workflow state with fulfilment method.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Created from the subscription manifest, not yet picked.</summary>
    Scheduled = 1,

    /// <summary>Picked and packed, waiting to be assigned to a route.</summary>
    Packed = 2,

    /// <summary>On a published route, waiting to be loaded and driven.</summary>
    Routed = 3,

    /// <summary>Loaded and on the road.</summary>
    OutForDelivery = 4,

    /// <summary>Completed. Rotation has advanced and add-ons have been consumed.</summary>
    Delivered = 5,

    /// <summary>Attempted but not completed (nobody home, refused, damaged).</summary>
    Failed = 6,

    /// <summary>Called off before it shipped.</summary>
    Canceled = 7
}
