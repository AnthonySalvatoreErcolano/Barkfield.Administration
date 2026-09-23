namespace Barkfield.Administration.Application.DataAccess.Allergies;

/// <summary>
/// An allergy, for the picker on the pet form and for display against a pet.
/// </summary>
public class AllergyDto
{
    public Guid Id { get; set; }
    public string AllergyName { get; set; } = string.Empty;
}
