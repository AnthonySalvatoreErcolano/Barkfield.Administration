using Dapper;
using System.Data;

namespace Barkfield.Administration.Infrastructure.Connections.Database;

/// <summary>
/// Dapper type handlers this application cannot work without.
/// </summary>
/// <remarks>
/// <para>
/// Registered from <see cref="SqlExecutor"/>'s static constructor rather than from DI. Dapper's
/// handler table is global static state, and <see cref="ISqlExecutor"/> is the only gateway to
/// the database — tying registration to that type means nothing can reach SQL Server without
/// the handlers being in place, including the bootstrap command and any throwaway harness that
/// news up an executor directly.
/// </para>
/// </remarks>
internal static class DapperTypeHandlers
{
    private static bool _registered;
    private static readonly object Gate = new();

    public static void Register()
    {
        if (_registered) return;

        lock (Gate)
        {
            if (_registered) return;

            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());

            _registered = true;
        }
    }

    /// <summary>
    /// Maps <see cref="TimeOnly"/> to and from a SQL <c>TIME</c> column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dapper 2.1.79 will read a <c>TIME</c> into a <see cref="TimeOnly"/>, but it refuses to
    /// send one the other way:
    /// <c>"The member PreferredWindowStart of type System.TimeOnly cannot be used as a parameter
    /// value"</c>. Without this, every write touching a delivery window fails at runtime while
    /// the read path looks perfectly healthy — which is exactly how it was found.
    /// </para>
    /// <para>
    /// The application uses <c>TIME(0)</c> in several places — a customer's preferred window, a
    /// delivery's requested window, a route stop's arrival — so this is registered once rather
    /// than worked around at each call site.
    /// </para>
    /// <para>
    /// Registering a handler for a value type also covers its nullable form, which is how most
    /// of these columns are declared.
    /// </para>
    /// </remarks>
    private sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override void SetValue(IDbDataParameter parameter, TimeOnly value)
        {
            parameter.DbType = DbType.Time;
            parameter.Value = value.ToTimeSpan();
        }

        public override TimeOnly Parse(object value) => value switch
        {
            TimeOnly time => time,
            TimeSpan span => TimeOnly.FromTimeSpan(span),

            // A TIME can surface as a DateTime through some providers and as text through others.
            DateTime dateTime => TimeOnly.FromDateTime(dateTime),
            string text => TimeOnly.Parse(text),

            _ => throw new DataException($"Cannot convert {value?.GetType().Name ?? "null"} to TimeOnly.")
        };
    }
}
