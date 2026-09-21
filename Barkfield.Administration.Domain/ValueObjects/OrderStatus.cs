using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.ValueObjects
{
    //Change this to separate packing and ordering
    public enum OrderStatus
    {
        Ordered,
        ReadyForPickUp,
        PickedUp,
        Reccuring,
        OutOfstock,
        Delivered,
        OrderedForDelivery,
        ReadyForDelivery,
        AutoShip,
        Received,
        PartiallyOrdered,
        Shipped,
        ForDelivery,
        SubscriptionPU
    }
}
