using Barkfield.Administration.Application.Exceptions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace Barkfield.Administration.Infrastructure.Connections.Database;

public interface IMultipleResultsReader : IDisposable
{
    Task<IEnumerable<T>> ReadAsync<T>();
    Task<T> ReadSingleAsync<T>();
    Task<T?> ReadSingleOrDefaultAsync<T>();
}

/// <summary>
/// The single gateway to the database. Centralises error translation, slow-query logging
/// and the audited-write transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>A connection is created per call and disposed with it.</b> Holding one connection for
/// the lifetime of the scoped executor looks cheaper but is wrong: the reader returned by
/// <see cref="QueryMultipleAsync"/> owns its connection and disposes it, which would kill
/// every later query on the same request. ADO.NET connection pooling makes per-call
/// connections cheap — "opening" one takes it from the pool.
/// </para>
/// <para>
/// <see cref="QueryMultipleAsync"/> is the exception to the disposal pattern: its connection
/// must outlive the method, so ownership passes to the returned reader. Callers must dispose
/// it, which is why every call site uses <c>using</c>.
/// </para>
/// </remarks>
public class SqlExecutor : ISqlExecutor
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<SqlExecutor> _logger;

    public SqlExecutor(ILogger<SqlExecutor> logger, ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _connectionFactory.CreateConnection();

        return await WrapPerformanceAndErrorsAsync(
            () => connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
            sql);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _connectionFactory.CreateConnection();

        return await WrapPerformanceAndErrorsAsync(
            () => connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
            sql);
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _connectionFactory.CreateConnection();

        return await WrapPerformanceAndErrorsAsync(
            () => connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
            sql);
    }

    /// <summary>
    /// Executes a query that JOINs two tables and maps them using a custom Func wrapper.
    /// </summary>
    public async Task<IEnumerable<TReturn>> QueryJoinAsync<TFirst, TSecond, TReturn>(
        string sql,
        Func<TFirst, TSecond, TReturn> map,
        object? parameters = null,
        string splitOn = "Id",
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

        return await WrapPerformanceAndErrorsAsync(
            () => connection.QueryAsync(command, map, splitOn: splitOn),
            sql);
    }

    public async Task<int> ExecuteWithAudit(
        string sql,
        object? parameters = null,
        string auditAction = "GeneralUpdate",
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _connectionFactory.CreateConnection();

        return await WrapPerformanceAndErrorsAsync(async () =>
        {
            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                var result = await connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken));

                // The audit row rides the same transaction, so the two roll back together.
                await CreateAuditAsync(connection, sql, parameters, auditAction, transaction, cancellationToken);

                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }, sql);
    }

    /// <summary>
    /// Runs several result sets in one round trip.
    /// </summary>
    /// <remarks>
    /// Ownership of the connection passes to the returned reader — <b>dispose it</b>.
    /// </remarks>
    public async Task<IMultipleResultsReader> QueryMultipleAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        IDbConnection connection = _connectionFactory.CreateConnection();

        try
        {
            var gridReader = await WrapPerformanceAndErrorsAsync(
                () => connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
                sql);

            return new DapperMultipleResultsReader(gridReader, connection);
        }
        catch
        {
            // The reader was never handed back, so nothing else will close this.
            connection.Dispose();
            throw;
        }
    }

    private async Task<T> WrapPerformanceAndErrorsAsync<T>(Func<Task<T>> action, string sql)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during query: {Sql}", sql);
            throw new DatabaseException("The database is currently unavailable.", ex);
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 500)
                _logger.LogWarning("Slow Query ({Ms}ms): {Sql}", sw.ElapsedMilliseconds, sql);
        }
    }

    private static async Task CreateAuditAsync(
        IDbConnection connection,
        string? sql,
        object? parameters,
        string auditAction,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        string jsonParams = parameters != null ? JsonSerializer.Serialize(parameters) : "{}";

        const string auditSql = @"
            INSERT INTO AuditLogs (Action, Query, JsonParameters, Timestamp)
            VALUES (@Action, @Sql, @JsonParams, @Now)";

        await connection.ExecuteAsync(new CommandDefinition(
            auditSql,
            new
            {
                Action = auditAction,
                Sql = sql,
                JsonParams = jsonParams,
                Now = DateTime.UtcNow
            },
            transaction: transaction,
            cancellationToken: cancellationToken));
    }

    public class DapperMultipleResultsReader(SqlMapper.GridReader reader, IDbConnection connection) : IMultipleResultsReader
    {
        public async Task<IEnumerable<T>> ReadAsync<T>() => await reader.ReadAsync<T>();
        public async Task<T> ReadSingleAsync<T>() => await reader.ReadSingleAsync<T>();
        public async Task<T?> ReadSingleOrDefaultAsync<T>() => await reader.ReadSingleOrDefaultAsync<T>();

        public void Dispose()
        {
            reader.Dispose();
            connection.Dispose();
        }
    }
}
