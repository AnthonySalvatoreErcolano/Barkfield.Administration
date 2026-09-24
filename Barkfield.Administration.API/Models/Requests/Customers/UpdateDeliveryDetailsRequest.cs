using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Customers;

/// <summary>
/// The driver-facing detail for a customer's stop.
/// </summary>
/// <remarks>
/// Separate from the customer edit because this is what dispatch maintains, not part of the
/// customer's identity — and because a form that does not collect a gate code must not be able
/// to clear one by omitting it.
/// </remarks>
public class UpdateDeliveryDetailsRequest
{
    /// <summary>Gate codes, "leave at side door", "dog in yard". Null or blank clears it.</summary>
    [MaxLength(1000)]
    public string? AccessNotes { get; set; }

    /// <summary>How long the stop takes, 0–480 minutes. Omit to leave unchanged.</summary>
    [Range(0, 480)]
    public int? ServiceDurationMinutes { get; set; }

    /// <summary>Both window ends are required together, or both omitted to clear the window.</summary>
    public TimeOnly? PreferredWindowStart { get; set; }

    public TimeOnly? PreferredWindowEnd { get; set; }
}
