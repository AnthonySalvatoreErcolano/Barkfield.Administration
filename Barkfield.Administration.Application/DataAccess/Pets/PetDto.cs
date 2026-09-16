using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Pets
{
    public class PetDto
    {
        public Guid Id { get; private set; }
        public Guid CustomerId { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public DateTime? Birthday { get; private set; }
        public PetType PetType { get; private set; }
        public string? Breed { get; private set; }
        public string? Notes { get; private set; }
        public string? PictureUrl { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
    }
}
