using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// A time-of-day window within which a delivery should land. Feeds the optimiser's
/// per-visit start/end constraint.
/// </summary>
public sealed record TimeWindow
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    private TimeWindow(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
            throw new DomainException($"A time window must end after it starts (got {start:HH\\:mm}–{end:HH\\:mm}).");

        Start = start;
        End = end;
    }

    public static TimeWindow Create(TimeOnly start, TimeOnly end) => new(start, end);

    public static TimeWindow Create(int startHour, int endHour) =>
        new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    public bool Contains(TimeOnly time) => time >= Start && time <= End;

    public int DurationMinutes => (int)(End - Start).TotalMinutes;

    public override string ToString() => $"{Start:HH\\:mm}–{End:HH\\:mm}";
}
