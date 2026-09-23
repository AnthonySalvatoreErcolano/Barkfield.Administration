using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Pets;

public class PetQueries : IPetQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    public PetQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PetDto?> GetByIdAsync(Guid petId, CancellationToken cancellationToken = default)
    {
        var pets = await LoadAsync("p.Id = @PetId", new { PetId = petId }, cancellationToken);

        return pets.FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<PetDto>> GetByCustomerIdAsync(
        Guid customerId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        string predicate = includeInactive
            ? "p.CustomerId = @CustomerId"
            : "p.CustomerId = @CustomerId AND p.IsActive = 1";

        return await LoadAsync(predicate, new { CustomerId = customerId }, cancellationToken);
    }

    /// <summary>
    /// Loads pets and their allergies in one round trip, then stitches them together in
    /// memory rather than issuing a query per pet.
    /// </summary>
    private async Task<IReadOnlyCollection<PetDto>> LoadAsync(
        string predicate,
        object parameters,
        CancellationToken cancellationToken)
    {
        string sql = $@"
            SELECT
                p.Id, p.CustomerId, p.Name, p.Birthday, p.PetType,
                p.Breed, p.Notes, p.PictureUrl, p.IsActive, p.CreatedAt, p.UpdatedAt
            FROM dbo.Pets p
            WHERE {predicate}
            ORDER BY p.Name;

            SELECT
                pa.PetId,
                a.Id,
                a.AllergyName
            FROM dbo.PetAllergies pa
            INNER JOIN dbo.Allergies a ON a.Id = pa.AllergyId
            INNER JOIN dbo.Pets p ON p.Id = pa.PetId
            WHERE {predicate}
            ORDER BY a.AllergyName;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        var pets = (await reader.ReadAsync<PetDto>()).ToList();
        if (pets.Count == 0) return pets;

        var allergyRows = await reader.ReadAsync<PetAllergyRow>();

        var byPet = allergyRows
            .GroupBy(r => r.PetId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<AllergyDto>)g
                    .Select(r => new AllergyDto { Id = r.Id, AllergyName = r.AllergyName })
                    .ToList());

        foreach (var pet in pets)
        {
            if (byPet.TryGetValue(pet.Id, out var allergies))
            {
                pet.Allergies = allergies;
            }
        }

        return pets;
    }

    /// <summary>Flat shape for the join, carrying the owning pet id alongside the allergy.</summary>
    private sealed class PetAllergyRow
    {
        public Guid PetId { get; set; }
        public Guid Id { get; set; }
        public string AllergyName { get; set; } = string.Empty;
    }
}
