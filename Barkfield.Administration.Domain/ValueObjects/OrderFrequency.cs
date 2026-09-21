using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.ValueObjects
{
    public enum FrequencyUnit
    {
        Days,
        Weeks,
        Months
    }
    public class OrderFrequency
    {
        public int Interval { get; private init; }
        public FrequencyUnit Unit { get; private init; }

        private OrderFrequency(int interval, FrequencyUnit unit)
        {
            if (interval <= 0) throw new ArgumentException("Interval must be greater than zero.");
            Interval = interval;
            Unit = unit;
        }

        public static OrderFrequency Every(int interval, FrequencyUnit unit) => new(interval, unit);

        /// <summary>
        /// Calculates the next delivery date from a given starting point based on this frequency.
        /// </summary>
        public DateTime CalculateNextDate(DateTime fromDate)
        {
            return Unit switch
            {
                FrequencyUnit.Days => fromDate.AddDays(Interval),
                FrequencyUnit.Weeks => fromDate.AddDays(Interval * 7),
                FrequencyUnit.Months => fromDate.AddMonths(Interval),
                _ => throw new NotImplementedException($"Unsupported time unit: {Unit}")
            };
        }

        public override string ToString() => $"{Interval} {Unit.ToString().ToLowerInvariant()}";
    }
}
