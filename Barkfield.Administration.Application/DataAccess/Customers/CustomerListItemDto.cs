namespace Barkfield.Administration.Application.DataAccess.Customers;

/// <summary>
/// One row on the customer dashboard. Deliberately narrow — the detail view is a
/// separate read.
/// </summary>
/// <remarks>
/// Public setters because Dapper populates this by column name.
/// </remarks>
public class CustomerListItemDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public string? SquareCustomerId { get; set; }
    public bool IsActive { get; set; }
    public int PetCount { get; set; }
    public int ActiveSubscriptionCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>False when the customer has no Square profile yet, so the UI can flag it.</summary>
    public bool IsSyncedToSquare => !string.IsNullOrWhiteSpace(SquareCustomerId);
}
