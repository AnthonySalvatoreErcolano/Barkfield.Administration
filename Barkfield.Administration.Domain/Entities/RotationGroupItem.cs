using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class RotationGroupItem
    {
        public int Id { get; set; }
        public int RotationGroupId { get; set; }
        public Guid ProductId { get; set; }
        public int SequenceOrder { get; set; }
    }
}
