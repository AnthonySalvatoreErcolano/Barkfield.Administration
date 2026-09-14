using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class PetAllergy
    {
        public Guid PetId { get; set; }
        public Guid AllergyId { get; set; }
    }
}
