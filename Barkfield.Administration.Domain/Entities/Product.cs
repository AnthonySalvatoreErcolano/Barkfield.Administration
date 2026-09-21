using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class Product
    {
        public Guid Id { get; set; }
        public Guid SubscriptionId { get; set; }
        public string SquareProductId { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }


        public bool IsRotating { get; set; }

        public Product()
        {

        }

        public Product(Guid subscriptionId, string productName, int quantity)
        {
            SubscriptionId = subscriptionId;
            ProductName = productName;
            Quantity = quantity;
        }
    }
}
