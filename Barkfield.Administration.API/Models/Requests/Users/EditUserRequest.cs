namespace Barkfield.Administration.API.Models.Requests.Users
{
    public class EditUserRequest
    {
        public Guid UserId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public IEnumerable<Guid> UserRoles { get; set; }
    }
}
