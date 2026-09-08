using Barkfield.Administration.Application.Exceptions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Barkfield.Administration.Infrastructure.Connections.Database
{
    public interface IMultipleResultsReader : IDisposable
    {
        Task<IEnumerable<T>> ReadAsync<T>();
        Task<T> ReadSingleAsync<T>();
        Task<T?> ReadSingleOrDefaultAsync<T>();
    }
    public class SqlExecutor : ISqlExecutor
    {
        private readonly IDbConnection _cnn;
        private readonly ILogger<SqlExecutor> _logger;

        public SqlExecutor(ILogger<SqlExecutor> logger, IDbConnection connection)
        {
            _cnn = connection;
            _logger = logger;
        }

        public async Task<int> ExecuteAsync(string sql, object? parameters = null)
        {
            return await WrapPerformanceAndErrorsAsync(() => _cnn.ExecuteAsync(sql, parameters), sql);
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
        {
            return await WrapPerformanceAndErrorsAsync(() => _cnn.QueryAsync<T>(sql, parameters), sql);
        }

        public async Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null)
        {
            return await WrapPerformanceAndErrorsAsync(() => _cnn.QuerySingleOrDefaultAsync<T>(sql, parameters), sql);
            
        }

        /// <summary>
        /// Executes a query that JOINs two tables and maps them using a custom Func wrapper.
        /// </summary>
        public async Task<IEnumerable<TReturn>> QueryJoinAsync<TFirst, TSecond, TReturn>(
            string sql,
            Func<TFirst, TSecond, TReturn> map,
            object? parameters = null,
            string splitOn = "Id")
        {
            return await WrapPerformanceAndErrorsAsync(() =>
                _cnn.QueryAsync(sql, map, parameters, splitOn: splitOn), sql);
        }

        public async Task<int> ExecuteWithAudit(string sql, object? parameters = null, string auditAction = "GeneralUpdate")
        {
            return await WrapPerformanceAndErrorsAsync(async () =>
            {
                if (_cnn.State != ConnectionState.Open) _cnn.Open();
                using var transaction = _cnn.BeginTransaction();
                try
                {
                    var result = await _cnn.ExecuteAsync(sql, parameters, transaction);

                    // Crucial: Passed transaction into audit so it rolls back if either block crashes
                    await CreateAuditAsync(sql, parameters, auditAction, transaction);

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

        public async Task<IMultipleResultsReader> QueryMultipleAsync(string sql, object? parameters = null)
        {
            var gridReader = await _cnn.QueryMultipleAsync(sql, parameters);
            return new DapperMultipleResultsReader(gridReader, _cnn);
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

        private async Task CreateAuditAsync(string? sql, object? parameters, string auditAction, IDbTransaction transaction)
        {
            string jsonParams = parameters != null ? JsonSerializer.Serialize(parameters) : "{}";

            const string auditSql = @"
            INSERT INTO AuditLogs (Action, Query, JsonParameters, Timestamp) 
            VALUES (@Action, @Sql, @JsonParams, @Now)";

            await _cnn.ExecuteAsync(auditSql, new
            {
                Action = auditAction,
                Sql = sql,
                JsonParams = jsonParams,
                Now = DateTime.UtcNow
            }, transaction);
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
}
