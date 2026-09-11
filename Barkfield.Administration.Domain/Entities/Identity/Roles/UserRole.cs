namespace Barkfield.Administration.Domain.Entities.Identity.Roles;

public class UserRole
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    private UserRole() { }

    public static UserRole Create(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId cannot be empty.");

        if (roleId == Guid.Empty)
            throw new DomainException("RoleId cannot be empty.");

        return new UserRole
        {
            UserId = userId,
            RoleId = roleId
        };
    }
}