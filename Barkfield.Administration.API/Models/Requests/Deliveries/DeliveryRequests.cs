using Barkfield.Administration.Domain.ValueObjects;
using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Deliveries;

/// <summary>Creates deliveries for every subscription due on one date.</summary>
public class GenerateDeliveriesRequest
{
    /// <summary>
    /// The day the deliveries are for. Cannot be in the past — catching up a missed day means
    /// generating for today, which picks up everything overdue.
    /// </summary>
    [Required]
    public DateTime DeliveryDate { get; set; }
}

/// <summary>One product on a one-off delivery.</summary>
public class DeliveryLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Creates a one-off delivery for a customer already in the system, on a day they are not
/// otherwise scheduled, without touching their subscription.
/// </summary>
public class CreateOneOffDeliveryRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    public DateTime ScheduledFor { get; set; }

    /// <summary>1 LocalDelivery, 2 Pickup, 3 Shipping.</summary>
    public FulfillmentMethod FulfillmentMethod { get; set; } = FulfillmentMethod.LocalDelivery;

    [Required, MinLength(1)]
    public IEnumerable<DeliveryLineRequest> Lines { get; set; } = [];

    public string? Notes { get; set; }
}

public class AddDeliveryLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

public class ChangeDeliveryLineQuantityRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

/// <summary>A staff note explaining a procurement decision. Optional throughout.</summary>
public class ProcurementNoteRequest
{
    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>
/// Records stock physically received and set aside.
/// </summary>
/// <remarks>
/// Omit <see cref="QuantityReceived"/> for the whole line. A number below the line quantity
/// leaves it partially received, which keeps it on the worklist.
/// </remarks>
public class ReceiveDeliveryLineRequest
{
    [Range(0, int.MaxValue)]
    public int? QuantityReceived { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>Swaps a line for a different product, keeping the original on the record.</summary>
public class SubstituteDeliveryLineRequest
{
    [Required]
    public Guid SubstituteProductId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

public class MarkDeliveredRequest
{
    /// <summary>Defaults to now when omitted.</summary>
    public DateTime? DeliveredOn { get; set; }
}

public class MarkDeliveryFailedRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class UpdateDeliveryNotesRequest
{
    public string? Notes { get; set; }
}

/// <summary>Overrides the customer's preferred window for this delivery only.</summary>
public class SetDeliveryWindowRequest
{
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

/// <summary>Overrides the customer's default stop duration for this delivery only.</summary>
public class SetServiceDurationRequest
{
    [Range(0, 480)]
    public int? Minutes { get; set; }
}
