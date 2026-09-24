using Barkfield.Administration.Domain.ValueObjects;
using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Subscriptions;

/// <summary>
/// Creates a subscription. It starts empty and in NewSignUp — add contents, then activate.
/// </summary>
public class CreateSubscriptionRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Optional staff name, e.g. "Raw food". Worth setting when the customer has more than one
    /// subscription; blank falls back to a name derived from the cadence and contents.
    /// </summary>
    [MaxLength(100)]
    public string? Name { get; set; }

    /// <summary>Any positive number. Combined with the unit: 8 weeks, 1 month, 12 days.</summary>
    [Range(1, int.MaxValue)]
    public int FrequencyInterval { get; set; } = 1;

    /// <summary>1 Days, 2 Weeks, 3 Months.</summary>
    [Required]
    public FrequencyUnit FrequencyUnit { get; set; } = FrequencyUnit.Weeks;

    /// <summary>Cannot be in the past. Any day of the week — off-cycle deliveries are normal.</summary>
    [Required]
    public DateTime FirstDeliveryDate { get; set; }

    /// <summary>1 LocalDelivery, 2 Pickup, 3 Shipping.</summary>
    public FulfillmentMethod FulfillmentMethod { get; set; } = FulfillmentMethod.LocalDelivery;
}

/// <summary>Sets or clears the staff-given name. Blank restores the derived fallback.</summary>
public class RenameSubscriptionRequest
{
    [MaxLength(100)]
    public string? Name { get; set; }
}

public class ChangeFrequencyRequest
{
    [Range(1, int.MaxValue)]
    public int FrequencyInterval { get; set; } = 1;

    [Required]
    public FrequencyUnit FrequencyUnit { get; set; } = FrequencyUnit.Weeks;

    /// <summary>
    /// True shifts the next delivery to match the new cadence, measured from the last delivery.
    /// False keeps the date already set, which is what you want when the next box is imminent.
    /// </summary>
    public bool RecalculateNextDelivery { get; set; } = true;
}

public class ChangeFulfillmentMethodRequest
{
    [Required]
    public FulfillmentMethod FulfillmentMethod { get; set; }
}

/// <summary>Moves the next delivery to a specific date without changing the cadence.</summary>
public class RescheduleRequest
{
    [Required]
    public DateTime NextDeliveryDate { get; set; }
}

/// <summary>
/// Pauses the subscription.
/// </summary>
/// <remarks>
/// <see cref="ResumeOn"/> is the vacation case. Leaving it null pauses open-endedly, which
/// relies on someone remembering — the dashboard's "due back" filter only finds dated pauses.
/// </remarks>
public class PauseSubscriptionRequest
{
    public DateTime? ResumeOn { get; set; }
}

/// <summary>Adds a product that ships every cycle. An existing line's quantity is raised instead.</summary>
public class AddSubscriptionItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

public class ChangeQuantityRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

public class RotationGroupNameRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

public class AddRotationItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Replaces the rotation order. Must list every item in the group exactly once.
/// </summary>
/// <remarks>
/// The product currently up next stays up next — reordering a rotation must not silently change
/// what the customer is about to receive.
/// </remarks>
public class ReorderRotationRequest
{
    [Required, MinLength(1)]
    public IEnumerable<Guid> ItemIdsInOrder { get; set; } = [];
}

/// <summary>Attaches a product to the next delivery only.</summary>
public class AddAddOnRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    /// <summary>Staff note, e.g. "customer asked for a sample".</summary>
    [MaxLength(500)]
    public string? Note { get; set; }
}
