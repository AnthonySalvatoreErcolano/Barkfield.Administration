namespace Barkfield.Administration.API.Models.Responses.Identity
{
    public class LoginResponse(
    Guid UserId,
    string Name,
    string Email,
    string Token,
    IEnumerable<string> Permissions
    );
}
