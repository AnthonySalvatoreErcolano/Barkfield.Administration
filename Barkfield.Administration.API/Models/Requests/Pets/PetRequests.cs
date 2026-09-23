using Barkfield.Administration.Domain.ValueObjects;
using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Pets;

/// <summary>
/// Adds a pet. The owning customer comes from the route.
/// </summary>
public class CreatePetRequest
{
    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>1 Dog, 2 Cat, 3 Other.</summary>
    [Required]
    public PetType PetType { get; set; }

    public DateTime? Birthday { get; set; }

    [MaxLength(100)]
    public string? Breed { get; set; }

    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? PictureUrl { get; set; }

    /// <summary>Ids from GET /api/allergies. Empty is normal — most pets have none.</summary>
    public IEnumerable<Guid> AllergyIds { get; set; } = [];
}

/// <summary>
/// Updates a pet. Allergies are replaced wholesale by <see cref="AllergyIds"/>.
/// </summary>
public class UpdatePetRequest
{
    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public PetType PetType { get; set; }

    public DateTime? Birthday { get; set; }

    [MaxLength(100)]
    public string? Breed { get; set; }

    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? PictureUrl { get; set; }

    public IEnumerable<Guid> AllergyIds { get; set; } = [];
}

public class CreateAllergyRequest
{
    [Required, MaxLength(100)]
    public string AllergyName { get; set; } = string.Empty;
}
