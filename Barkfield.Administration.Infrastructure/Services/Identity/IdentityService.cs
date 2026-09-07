using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Barkfield.Administration.Infrastructure.Services.Identity
{
    internal class IdentityService(PasswordHasher passwordHasher, IOptions<JwtSettings> options, ISqlExecutor sqlExecutor) : IIdentityService
    {
        private readonly JwtSettings _jwtOptions = options.Value;

        public async Task<AuthenticationResult?> LoginAsync(string email,string password)
        {


            LoginDto? user = await sqlExecutor.QuerySingleAsync<LoginDto>(@"SELECT Id, Name, Email, PasswordHash, IsActive 
            FROM Users 
            WHERE Email = @Email AND IsActive=1;", new { Email = email });

            if (user == null) return null;

            var verificationResult = passwordHasher.VerifyPassword( password,user.PasswordHash);

            if (!verificationResult) return null;

            var token = GenerateJwtToken(user.Id, email);

            return new AuthenticationResult(token, email, user.Id);
        }

        public async Task<Guid?> RegisterAsync(string email, string password, string name)
        {
            string hashedPassword = passwordHasher.HashPassword(password);
            // Dapper execution here...

            return Guid.NewGuid();
        }

        private string GenerateJwtToken(Guid userId, string email)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email)
                ]),
                Expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes), // <-- Fixed here
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _jwtOptions.Issuer,
                Audience = _jwtOptions.Audience,
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
