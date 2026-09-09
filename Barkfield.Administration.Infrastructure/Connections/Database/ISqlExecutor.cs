using System.Data;

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
    }
}