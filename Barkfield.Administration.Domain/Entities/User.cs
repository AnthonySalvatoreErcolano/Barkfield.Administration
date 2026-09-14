using Barkfield.Administration.Domain.Shared.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class User
    {
        private readonly List<Guid> _roleIds = [];
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public IReadOnlyCollection<Guid> RoleIds => _roleIds.AsReadOnly();

        public User()
        {
                
        }

        public static User Create(string name, string email, string passwordHash, IEnumerable<Guid> roleIds)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("User name cannot be empty.");

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                throw new DomainException("A valid email address is required.");

            var roleList = roleIds?.Distinct().ToList() ?? [];
            if (roleList.Count == 0)
                throw new DomainException("A user must be assigned at least one role.");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                Email = email.ToLowerInvariant().Trim(),
                PasswordHash = passwordHash,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            user._roleIds.AddRange(roleList);
            return user;
        }
        public static User FromDto(Guid id, string name, string email, string passwordHash, bool isActive, DateTime createdAt, IEnumerable<Guid> roleIds)
        {
            if (id == Guid.Empty)
                throw new DomainException("Invalid user ID.");

            var user = new User
            {
                Id = id,
                Name = name,
                Email = email,
                PasswordHash = passwordHash,
                IsActive = isActive,
                CreatedAt = createdAt
            };

            if (roleIds != null)
            {
                user._roleIds.AddRange(roleIds.Distinct());
            }

            return user;
        }
        public void AssignRole(Guid roleId)
        {
            if (roleId == Guid.Empty)
                throw new DomainException("Invalid role ID.");

            if (!_roleIds.Contains(roleId))
                _roleIds.Add(roleId);
        }

        public void RemoveRole(Guid roleId)
        {
            if (_roleIds.Count <= 1 && _roleIds.Contains(roleId))
                throw new DomainException("Cannot remove the last role from a user.");

            _roleIds.Remove(roleId);
        }

        public void UpdateProfile(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("User name cannot be empty.");

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                throw new DomainException("A valid email address is required.");

            Name = name.Trim();
            Email = email.ToLowerInvariant().Trim();
        }

        public void SyncRoles(IEnumerable<Guid> roleIds)
        {
            var roleList = roleIds?.Distinct().ToList() ?? [];
            if (roleList.Count == 0)
                throw new DomainException("A user must be assigned at least one role.");

            _roleIds.Clear();
            _roleIds.AddRange(roleList);
        }

       
    }
}
