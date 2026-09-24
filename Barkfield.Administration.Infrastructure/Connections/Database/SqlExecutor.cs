using Barkfield.Administration.Application.Exceptions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
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
/// The single gateway to the database. Centralises error translation, slow-query logging,
/// the audited-write transaction, and the multi-call transaction scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>A connection is created per call and disposed with it</b> — unless a transaction scope is
/// open, in which case every call shares the scope's connection and none of them dispose it.
/// Holding one connection for the lifetime of the executor unconditionally looks cheaper but is
/// wrong: the reader returned by <see cref="QueryMultipleAsync"/> owns its connection and
/// disposes it, which would kill every later query on the same request. ADO.NET pooling makes
/// per-call connections cheap — "opening" one takes it from the pool.
/// </para>
/// <para>
/// <see cref="QueryMultipleAsync"/> is the exception to the disposal pattern: outside a scope
/// its connection must outlive the method, so ownership passes to the returned reader. Callers
/// must dispose it, which is why every call site uses <c>using</c>. Inside a scope the reader
/// owns nothing, because the scope does.
/// </para>
/// <para>
/// This type is registered <b>scoped</b>, so every command and query object in one request
/// shares the same instance. That is what lets <see cref="BeginTransactionAsync"/> work without
/// threading a transaction through every signature — and it is also why a scope must not be used
/// for concurrent work, since one connection runs one command at a time.
/// </para>
/// </remarks>
public class SqlExecutor : ISqlExecutor
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<SqlExecutor> _logger;

    /// <summary>The open transaction scope, or null when each call stands alone.</summary>
    private TransactionScope? _scope;

    /// <summary>
    /// Registers the Dapper type handlers the application depends on.
    /// </summary>
    /// <remarks>
    /// Here rather than in DI because Dapper's handler table is global static state and this
    /// type is the only gateway to the database. Tying registration to it means nothing can
    /// reach SQL Server without the handlers, including the bootstrap command.
    /// </remarks>
    static SqlExecutor() => DapperTypeHandlers.Register();

    public SqlExecutor(ILogger<SqlExecutor> logger, ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public bool HasActiveTransaction => _scope is { IsActive: true };

    public async Task<ISqlTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_scope is { IsActive: true })
        {
            throw new InvalidOperationException(
                "A transaction is already open on this executor. Scopes do not nest — one connection "
                + "cannot hold two transactions, and quietly joining the outer one would make a "
                + "rolled-back inner scope a lie.");
        }

        IDbConnection connection = _connectionFactory.CreateConnection();

        try
        {
            if (connection is DbConnection asyncConnection)
            {
                await asyncConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }

            IDbTransaction transaction = connection.BeginTransaction();

            _scope = new TransactionScope(this, connection, transaction, _logger);

            return _scope;
        }
        catch (SqlException ex)
        {
            connection.Dispose();

            _logger.LogError(ex, "Failed to open a database transaction.");

            throw new DatabaseException("The database is currently unavailable.", ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using Lease lease = Acquire();

        return await WrapPerformanceAndErrorsAsync(
            () => lease.Connection.ExecuteAsync(Command(sql, parameters, lease, cancellationToken)),
            sql);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using Lease lease = Acquire();

        return await WrapPerformanceAndErrorsAsync(
            () => lease.Connection.QueryAsync<T>(Command(sql, parameters, lease, cancellationToken)),
            sql);
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        using Lease lease = Acquire();

        return await WrapPerformanceAndErrorsAsync(
            () => lease.Connection.QuerySingleOrDefaultAsync<T>(Command(sql, parameters, lease, cancellationToken)),
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
        using Lease lease = Acquire();

        CommandDefinition command = Command(sql, parameters, lease, cancellationToken);

        return await WrapPerformanceAndErrorsAsync(
            () => lease.Connection.QueryAsync(command, map, splitOn: splitOn),
            sql);
    }

    public async Task<int> ExecuteWithAudit(
        string sql,
        object? parameters = null,
        string auditAction = "GeneralUpdate",
        CancellationToken cancellationToken = default)
    {
        // Inside a scope the caller owns the transaction, so this rides it and commits nothing:
        // the audit row and the write still land or fail together, which is the point.
        if (_scope is { IsActive: true })
        {
            using Lease enlisted = Acquire();

            return await WrapPerformanceAndErrorsAsync(async () =>
            {
                int affected = await enlisted.Connection.ExecuteAsync(
                    Command(sql, parameters, enlisted, cancellationToken));

                await CreateAuditAsync(
                    enlisted.Connection, sql, parameters, auditAction, enlisted.Transaction, cancellationToken);

                return affected;
            }, sql);
        }

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
    /// Outside a transaction scope, ownership of the connection passes to the returned reader —
    /// <b>dispose it</b>. Inside a scope the connection belongs to the scope and the reader
    /// leaves it alone, so disposing the reader early cannot break the rest of the transaction.
    /// </remarks>
    public async Task<IMultipleResultsReader> QueryMultipleAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        Lease lease = Acquire();

        try
        {
            var gridReader = await WrapPerformanceAndErrorsAsync(
                () => lease.Connection.QueryMultipleAsync(Command(sql, parameters, lease, cancellationToken)),
                sql);

            return new DapperMultipleResultsReader(gridReader, lease.Connection, lease.OwnsConnection);
        }
        catch
        {
            // The reader was never handed back, so nothing else will close this.
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Supplies the connection and transaction for one call: the scope's if one is open,
    /// otherwise a fresh connection this call owns and disposes.
    /// </summary>
    private Lease Acquire() =>
        _scope is { IsActive: true } scope
            ? new Lease(scope.Connection, scope.Transaction, ownsConnection: false)
            : new Lease(_connectionFactory.CreateConnection(), null, ownsConnection: true);

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        in Lease lease,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction: lease.Transaction, cancellationToken: cancellationToken);

    private void ClearScope(TransactionScope scope)
    {
        if (ReferenceEquals(_scope, scope)) _scope = null;
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
        IDbTransaction? transaction,
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

    /// <summary>
    /// One call's borrowed connection. Disposes it only if the call opened it.
    /// </summary>
    private readonly struct Lease(IDbConnection connection, IDbTransaction? transaction, bool ownsConnection) : IDisposable
    {
        public IDbConnection Connection { get; } = connection;
        public IDbTransaction? Transaction { get; } = transaction;
        public bool OwnsConnection { get; } = ownsConnection;

        public void Dispose()
        {
            if (OwnsConnection) Connection.Dispose();
        }
    }

    /// <summary>
    /// The transaction every call on the executor joins until it ends.
    /// </summary>
    private sealed class TransactionScope(
        SqlExecutor executor,
        IDbConnection connection,
        IDbTransaction transaction,
        ILogger logger) : ISqlTransactionScope
    {
        private bool _committed;
        private bool _disposed;

        public IDbConnection Connection { get; } = connection;
        public IDbTransaction Transaction { get; } = transaction;

        public bool IsActive => !_disposed && !_committed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ISqlTransactionScope));
            if (_committed) throw new InvalidOperationException("This transaction has already been committed.");

            try
            {
                Transaction.Commit();
                _committed = true;
            }
            catch (SqlException ex)
            {
                logger.LogError(ex, "Failed to commit a database transaction.");

                throw new DatabaseException("The database is currently unavailable.", ex);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;

            _disposed = true;

            // Uncommitted means something went wrong, so undo it. Rolling back is the default
            // precisely so a thrown exception does not need a handler at every call site.
            if (!_committed)
            {
                try
                {
                    Transaction.Rollback();
                }
                catch (Exception ex)
                {
                    // XACT_ABORT inside a batch can have aborted the transaction already, in
                    // which case there is nothing left to roll back. The work is undone either
                    // way, so this is worth recording and not worth throwing over — especially
                    // as it usually happens while another exception is already propagating.
                    logger.LogWarning(ex, "Rolling back a database transaction failed; it was most likely already aborted by the server.");
                }
            }

            Transaction.Dispose();
            Connection.Dispose();

            executor.ClearScope(this);

            return ValueTask.CompletedTask;
        }
    }

    public class DapperMultipleResultsReader(
        SqlMapper.GridReader reader,
        IDbConnection connection,
        bool ownsConnection) : IMultipleResultsReader
    {
        public async Task<IEnumerable<T>> ReadAsync<T>() => await reader.ReadAsync<T>();
        public async Task<T> ReadSingleAsync<T>() => await reader.ReadSingleAsync<T>();
        public async Task<T?> ReadSingleOrDefaultAsync<T>() => await reader.ReadSingleOrDefaultAsync<T>();

        public void Dispose()
        {
            reader.Dispose();

            // Inside a transaction scope the connection belongs to the scope and must outlive
            // this reader — disposing it here would kill the rest of the transaction.
            if (ownsConnection) connection.Dispose();
        }
    }
}
