namespace Barkfield.Administration.Infrastructure.Connections.Database
{
    public interface ISqlExecutor
    {
        Task<int> ExecuteAsync(string sql, object? parameters = null);
        Task<int> ExecuteWithAudit(string sql, object? parameters = null, string auditAction = "GeneralUpdate");
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null);
        Task<IEnumerable<TReturn>> QueryJoinAsync<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object? parameters = null, string splitOn = "Id");
        Task<IMultipleResultsReader> QueryMultipleAsync(string sql, object? parameters = null);
        Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null);

    }
}