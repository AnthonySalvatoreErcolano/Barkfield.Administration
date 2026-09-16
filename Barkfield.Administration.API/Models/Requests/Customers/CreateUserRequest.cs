namespace Barkfield.Administration.API.Models.Requests.Customers
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public IEnumerable<Guid> UserRoles { get; set; }
    }
}
