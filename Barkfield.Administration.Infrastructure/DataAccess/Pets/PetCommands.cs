using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Pets;

/// <summary>
/// Writes for pets and their allergy links.
/// </summary>
/// <remarks>
/// A pet and its allergies land together in one T-SQL transaction. A half-applied allergy
/// change is worse than a failed one here: the whole point of recording allergies is that
/// staff trust them when packing a box.
///
/// Allergy ids are passed as a comma-separated string and expanded with STRING_SPLIT, which
/// keeps the multi-row insert inside the single statement.
/// </remarks>
public class PetCommands : IPetCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public PetCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(Pet pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);

        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            INSERT INTO dbo.Pets
                (Id, CustomerId, Name, Birthday, PetType, Breed, Notes, PictureUrl, IsActive, CreatedAt)
            VALUES
                (@Id, @CustomerId, @Name, @Birthday, @PetType, @Breed, @Notes, @PictureUrl, @IsActive, @CreatedAt);

            INSERT INTO dbo.PetAllergies (PetId, AllergyId)
            SELECT @Id, CAST(value AS UNIQUEIDENTIFIER)
            FROM STRING_SPLIT(@AllergyIds, ',')
            WHERE LTRIM(RTRIM(value)) <> '';

            COMMIT TRANSACTION;";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(pet), cancellationToken);
    }

    public async Task UpdateAsync(Pet pet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);

        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE dbo.Pets
               SET Name       = @Name,
                   Birthday   = @Birthday,
                   PetType    = @PetType,
                   Breed      = @Breed,
                   Notes      = @Notes,
                   PictureUrl = @PictureUrl,
                   IsActive   = @IsActive,
                   UpdatedAt  = @UpdatedAt
             WHERE Id = @Id;

            DELETE FROM dbo.PetAllergies WHERE PetId = @Id;

            INSERT INTO dbo.PetAllergies (PetId, AllergyId)
            SELECT @Id, CAST(value AS UNIQUEIDENTIFIER)
            FROM STRING_SPLIT(@AllergyIds, ',')
            WHERE LTRIM(RTRIM(value)) <> '';

            COMMIT TRANSACTION;";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(pet), cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid petId, bool isActive, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Pets
               SET IsActive  = @IsActive,
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @PetId;";

        int affected = await _sqlExecutor.ExecuteAsync(
            sql, new { PetId = petId, IsActive = isActive }, cancellationToken);

        return affected > 0;
    }

    private static object ToParameters(Pet pet) => new
    {
        pet.Id,
        pet.CustomerId,
        pet.Name,
        pet.Birthday,
        PetType = (byte)pet.PetType,
        pet.Breed,
        pet.Notes,
        pet.PictureUrl,
        pet.IsActive,
        pet.CreatedAt,
        UpdatedAt = pet.UpdatedAt ?? DateTime.UtcNow,
        AllergyIds = string.Join(',', pet.AllergyIds.Select(id => id.ToString()))
    };
}
