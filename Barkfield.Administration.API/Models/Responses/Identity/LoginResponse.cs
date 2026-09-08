namespace Barkfield.Administration.API.Models.Responses.Identity
{
        public class LoginResponse
        {
            public required string AccessToken { get; set; }
            public required string Email { get; set; }
            public required Guid UserId { get; set; }
        }
}
