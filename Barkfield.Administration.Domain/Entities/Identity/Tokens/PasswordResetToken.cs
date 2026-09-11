using Barkfield.Administration.Domain.Shared.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities.Identity.Tokens
{
    public class PasswordResetToken
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public string TokenHash { get; private set; }
        public DateTime ExpiresAt { get; private set; }
        public bool IsUsed { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? UsedAt { get; private set; }

        private PasswordResetToken() { }

        public static PasswordResetToken Create(Guid userId, string tokenHash, TimeSpan validityDuration)
        {
            if (userId == Guid.Empty)
                throw new DomainException("UserId cannot be empty.");

            if (string.IsNullOrWhiteSpace(tokenHash))
                throw new DomainException("Token hash is required.");

            return new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.Add(validityDuration),
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };
        }

        public void MarkAsUsed()
        {
            if (IsUsed)
                throw new DomainException("Password reset token has already been used.");

            if (DateTime.UtcNow > ExpiresAt)
                throw new DomainException("Password reset token has expired.");

            IsUsed = true;
            UsedAt = DateTime.UtcNow;
        }
    }
}
