namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Where a single delivery sits in the packing and dispatch workflow.
/// Replaces the old OrderStatus enum, which mixed workflow state with fulfilment method.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Created from the subscription manifest, not yet picked.</summary>
    Scheduled = 1,

    /// <summary>Picked and packed, waiting to go out.</summary>
    Packed = 2,

    /// <summary>Handed to the driver / on a dispatched route.</summary>
    OutForDelivery = 3,

    /// <summary>Completed. Rotation has advanced and add-ons have been consumed.</summary>
    Delivered = 4,

    /// <summary>Attempted but not completed (nobody home, refused, damaged).</summary>
    Failed = 5,

    /// <summary>Called off before it shipped.</summary>
    Canceled = 6
}
