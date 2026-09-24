using Barkfield.Administration.Application.DataAccess.Pets;

namespace Barkfield.Administration.Application.DataAccess.Customers;

/// <summary>
/// A single customer with their pets, for the customer detail and edit pages.
/// </summary>
/// <remarks>
/// Pets are bundled because every customer page needs them and the composition layer
/// would otherwise pay a second round trip for them. Subscriptions and deliveries are
/// deliberately not included — they are larger and not always needed.
/// </remarks>
public class CustomerDetailDto
{
    public Guid Id { get; set; }
    public string? SquareCustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Notes { get; set; }

    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }

    /// <summary>Driver-facing instructions — gate codes, "leave at side door", "dog in yard".</summary>
    public string? AccessNotes { get; set; }

    /// <summary>How long the stop takes. Sent to the routing provider per order.</summary>
    public int ServiceDurationMinutes { get; set; }

    public TimeOnly? PreferredWindowStart { get; set; }
    public TimeOnly? PreferredWindowEnd { get; set; }

    /// <summary>Only present once a routing provider has geocoded the address.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyCollection<PetDto> Pets { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsSyncedToSquare => !string.IsNullOrWhiteSpace(SquareCustomerId);
    public bool HasAddress => !string.IsNullOrWhiteSpace(Street) || !string.IsNullOrWhiteSpace(City);

    /// <summary>
    /// False when this customer cannot be put on a driven route. Pickup customers legitimately
    /// have no address, so this is information for the dispatch screen, not an error.
    /// </summary>
    public bool CanReceiveLocalDelivery => HasAddress;

    public bool IsGeocoded => Latitude.HasValue && Longitude.HasValue;
}
