using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Pets;

/// <summary>
/// A pet as shown on a customer's page. Public setters because Dapper populates this.
/// </summary>
public class PetDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? Birthday { get; set; }
    public PetType PetType { get; set; }
    public string? Breed { get; set; }
    public string? Notes { get; set; }
    public string? PictureUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string PetTypeName => PetType.ToString();
}
