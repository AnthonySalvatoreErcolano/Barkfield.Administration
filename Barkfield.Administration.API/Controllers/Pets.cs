using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Pets;
using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Pets and their allergies.
/// </summary>
/// <remarks>
/// Pets belong to a customer, so creating and listing them hangs off the customer route.
/// An individual pet is addressed directly by id, since the caller already has it.
///
/// Pets are customer data, so these reuse the customer permissions rather than introducing
/// a parallel set staff would have to be granted separately.
/// </remarks>
[ApiController]
[Authorize]
public class PetsController(IPetQueries petQueries, PetService petService) : ControllerBase
{
    private readonly IPetQueries _petQueries = petQueries;
    private readonly PetService _petService = petService;

    /// <summary>
    /// Returns a customer's pets, each with their allergies.
    /// </summary>
    /// <param name="includeInactive">Includes pets that have been deactivated.</param>
    [HttpGet("api/customers/{customerId:guid}/pets")]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<PetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPetsForCustomer(
        [FromRoute] Guid customerId,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PetDto> pets =
            await _petQueries.GetByCustomerIdAsync(customerId, includeInactive, cancellationToken);

        return Ok(pets);
    }

    /// <summary>
    /// Returns one pet with its allergies.
    /// </summary>
    [HttpGet("api/pets/{petId:guid}", Name = nameof(GetPetById))]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(PetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPetById([FromRoute] Guid petId, CancellationToken cancellationToken)
    {
        PetDto? pet = await _petQueries.GetByIdAsync(petId, cancellationToken);

        if (pet is null)
        {
            return NotFound(new { message = $"Pet with ID '{petId}' was not found." });
        }

        return Ok(pet);
    }

    /// <summary>
    /// Adds a pet to a customer.
    /// </summary>
    [HttpPost("api/customers/{customerId:guid}/pets")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePet(
        [FromRoute] Guid customerId,
        [FromBody] CreatePetRequest request,
        CancellationToken cancellationToken)
    {
        Guid petId = await _petService.CreatePetAsync(
            customerId,
            request.Name,
            request.PetType,
            request.Birthday,
            request.Breed,
            request.Notes,
            request.PictureUrl,
            request.AllergyIds,
            cancellationToken);

        return CreatedAtRoute(nameof(GetPetById), new { petId }, petId);
    }

    /// <summary>
    /// Updates a pet and replaces its allergy list.
    /// </summary>
    /// <remarks>
    /// Allergies are replaced wholesale, not merged — the form shows the full list, so
    /// sending it back is what the user means. An empty list clears them.
    /// </remarks>
    [HttpPut("api/pets/{petId:guid}")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePet(
        [FromRoute] Guid petId,
        [FromBody] UpdatePetRequest request,
        CancellationToken cancellationToken)
    {
        await _petService.UpdatePetAsync(
            petId,
            request.Name,
            request.PetType,
            request.Birthday,
            request.Breed,
            request.Notes,
            request.PictureUrl,
            request.AllergyIds,
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deactivates a pet. A soft delete — the record is kept and can be restored.
    /// </summary>
    [HttpDelete("api/pets/{petId:guid}")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivatePet([FromRoute] Guid petId, CancellationToken cancellationToken)
    {
        await _petService.DeactivatePetAsync(petId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Restores a previously deactivated pet.
    /// </summary>
    [HttpPost("api/pets/{petId:guid}/reactivate")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivatePet([FromRoute] Guid petId, CancellationToken cancellationToken)
    {
        await _petService.ReactivatePetAsync(petId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Returns every known allergy, for the picker on the pet form.
    /// </summary>
    [HttpGet("api/allergies")]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<AllergyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllergies(
        [FromServices] IAllergyQueries allergyQueries,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<AllergyDto> allergies = await allergyQueries.GetAllAsync(cancellationToken);

        return Ok(allergies);
    }

    /// <summary>
    /// Adds an allergy to the shared list, or returns the existing one with that name.
    /// </summary>
    /// <remarks>
    /// The seeded list will not cover everything, so staff need a way to add one without a
    /// deployment. Returns the id either way, so the client can select it immediately.
    /// </remarks>
    [HttpPost("api/allergies")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddAllergy(
        [FromBody] CreateAllergyRequest request,
        CancellationToken cancellationToken)
    {
        Guid allergyId = await _petService.AddAllergyToCatalogAsync(request.AllergyName, cancellationToken);

        return Ok(allergyId);
    }
}
