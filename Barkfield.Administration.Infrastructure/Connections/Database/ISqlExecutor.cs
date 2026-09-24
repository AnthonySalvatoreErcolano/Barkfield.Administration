namespace Barkfield.Administration.Infrastructure.Connections.Database
{
    public interface ISqlExecutor
    {
        Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
        Task<int> ExecuteWithAudit(string sql, object? parameters = null, string auditAction = "GeneralUpdate", CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<TReturn>> QueryJoinAsync<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object? parameters = null, string splitOn = "Id", CancellationToken cancellationToken = default);
        Task<IMultipleResultsReader> QueryMultipleAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
        Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a transaction that every subsequent call on this executor joins, until the
        /// returned scope is committed or disposed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For work spanning more than one aggregate, where a partial write would be worse than
        /// no write. See <see cref="ISqlTransactionScope"/>.
        /// </para>
        /// <para>
        /// Scopes do not nest — a second call while one is open throws, because one connection
        /// cannot hold two transactions and silently sharing the outer one would make a "rolled
        /// back" inner scope a lie.
        /// </para>
        /// </remarks>
        Task<ISqlTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>True while a transaction opened by <see cref="BeginTransactionAsync"/> is in force.</summary>
        bool HasActiveTransaction { get; }
    }
}
