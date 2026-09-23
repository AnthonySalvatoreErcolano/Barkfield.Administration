using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Pets;

/// <summary>
/// A pet with its allergies.
/// </summary>
/// <remarks>
/// Allergies are bundled rather than fetched separately because they are the reason staff
/// look at a pet at all — they are checked before a protein goes in a box, so a pet without
/// them is not much use on screen.
/// </remarks>
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
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyCollection<AllergyDto> Allergies { get; set; } = [];

    /// <summary>Enum name, so the UI does not have to keep its own mapping of the numbers.</summary>
    public string PetTypeName => PetType.ToString();

    public IReadOnlyCollection<Guid> AllergyIds => Allergies.Select(a => a.Id).ToList();

    public bool HasAllergies => Allergies.Count > 0;

    /// <summary>Age in whole years, when a birthday is recorded.</summary>
    public int? AgeYears
    {
        get
        {
            if (Birthday is null) return null;

            DateTime today = DateTime.UtcNow.Date;
            int age = today.Year - Birthday.Value.Year;

            if (Birthday.Value.Date > today.AddYears(-age)) age--;

            return age < 0 ? null : age;
        }
    }
}
