namespace Barkfield.Administration.API.Models.Requests
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public Guid UserRole { get; set; }
    }
}
