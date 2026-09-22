using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.ValueObjects;

public enum FrequencyUnit
{
    Days = 1,
    Weeks = 2,
    Months = 3
}

/// <summary>
/// How often a subscription ships, as an interval plus a unit ("every 4 weeks").
/// A record so two equal cadences compare equal.
/// </summary>
public sealed record OrderFrequency
{
    public int Interval { get; }
    public FrequencyUnit Unit { get; }

    private OrderFrequency(int interval, FrequencyUnit unit)
    {
        if (interval <= 0)
            throw new DomainException("Frequency interval must be greater than zero.");

        if (!Enum.IsDefined(unit))
            throw new DomainException($"Unsupported frequency unit: {unit}.");

        Interval = interval;
        Unit = unit;
    }

    public static OrderFrequency Every(int interval, FrequencyUnit unit) => new(interval, unit);

    public static OrderFrequency Weekly() => new(1, FrequencyUnit.Weeks);

    public static OrderFrequency EveryNWeeks(int weeks) => new(weeks, FrequencyUnit.Weeks);

    public static OrderFrequency Monthly() => new(1, FrequencyUnit.Months);

    /// <summary>
    /// Calculates the next delivery date from a given starting point based on this frequency.
    /// </summary>
    public DateTime CalculateNextDate(DateTime fromDate) => Unit switch
    {
        FrequencyUnit.Days => fromDate.AddDays(Interval),
        FrequencyUnit.Weeks => fromDate.AddDays(Interval * 7),
        FrequencyUnit.Months => fromDate.AddMonths(Interval),
        _ => throw new DomainException($"Unsupported time unit: {Unit}")
    };

    public override string ToString() =>
        Interval == 1
            ? $"every {Unit.ToString().TrimEnd('s').ToLowerInvariant()}"
            : $"every {Interval} {Unit.ToString().ToLowerInvariant()}";
}
