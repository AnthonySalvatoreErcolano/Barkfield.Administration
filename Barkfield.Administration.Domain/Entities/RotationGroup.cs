using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class RotationGroup
    {
        public int Id { get; set; }
        public Guid SubscriptionId { get; set; }
        public string GroupName { get; set; }
        public List<RotationGroupItem> RotationGroupItems { get; set; } = new List<RotationGroupItem>();


    }
}
