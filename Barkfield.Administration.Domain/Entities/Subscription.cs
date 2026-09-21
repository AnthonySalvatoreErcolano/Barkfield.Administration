using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities
{
    public class Subscription
    {
        public Guid Id { get; private init; }
        public Guid CustomerId { get; private init; }
        public OrderStatus OrderStatus { get; private set; }
        public SubscriptionStatus SubscriptionStatus { get; set; }
        public OrderFrequency Frequency { get; private set; }
        public bool HasPaid { get; set; }
        public bool AstroCompleted { get; set; }
        public DateTime NextDeliveryDate { get; private set; }
        public DateTime? LastDeliveryDate { get; private set; }
        public DateTime SignUpDate { get; set; }



        private readonly List<Product> _items = new();
        public IReadOnlyCollection<Product> Items => _items.AsReadOnly();


        public static Subscription Create(Guid customerId, OrderFrequency frequency, DateTime firstDeliveryDate)
        {
            if (customerId == Guid.Empty) throw new ArgumentException("A valid CustomerId is required.");
            if (firstDeliveryDate.Date < DateTime.UtcNow.Date) throw new ArgumentException("First delivery date cannot be in the past.");
            ArgumentNullException.ThrowIfNull(frequency);

            return new Subscription
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                OrderStatus = OrderStatus.AutoShip,
                Frequency = frequency,
                NextDeliveryDate = firstDeliveryDate.Date
            };
        }

        public void AddItem(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            if (!_items.Any(p => p.Id == product.Id)) _items.Add(product);
        }

        public void RemoveItem(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            _items.RemoveAll(p => p.Id == product.Id);
        }

        public void AddProducts(IEnumerable<Product> products)
        {
            ArgumentNullException.ThrowIfNull(products);
            _items.AddRange(products);
        }

        /// <summary>
        /// Modifies the shipping interval cadence and automatically shifts the next delivery date accordingly.
        /// </summary>
        public void ChangeFrequency(OrderFrequency newFrequency, bool recalculateNextDelivery = true)
        {
            Frequency = newFrequency ?? throw new ArgumentNullException(nameof(newFrequency));

            if (recalculateNextDelivery)
            {
                var baseDate = LastDeliveryDate ?? DateTime.UtcNow;
                NextDeliveryDate = Frequency.CalculateNextDate(baseDate).Date;
            }
        }


        public void RecordSuccessfulDelivery(DateTime deliveryDate)
        {
            LastDeliveryDate = deliveryDate.Date;
            NextDeliveryDate = Frequency.CalculateNextDate(deliveryDate).Date;
        }
    }
}
