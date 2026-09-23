namespace Barkfield.Administration.API.Models.Responses.Customers;

/// <summary>
/// Returned from customer create and update.
/// </summary>
/// <remarks>
/// A write succeeds locally even when Square is unreachable, so the caller is told
/// separately whether the Square profile is in step. <c>SquareSynced = false</c> is not an
/// error: the customer is saved, and the UI should show a "not synced to Square" badge
/// with the option to retry.
/// </remarks>
public class CustomerWriteResponse
{
    public Guid CustomerId { get; set; }
    public bool SquareSynced { get; set; }
    public string? SquareError { get; set; }
}
