using Barkfield.Administration.Application.Common;

namespace Barkfield.Administration.Infrastructure.Connections.Database;

/// <summary>
/// Implements the Application layer's transaction contract over <see cref="ISqlExecutor"/>.
/// </summary>
/// <remarks>
/// A thin adapter, and deliberately so. The executor already owns the connection and the
/// enlisting behaviour; this only exposes it to application services, which cannot reference
/// Infrastructure types. Both must be resolved from the same DI scope for the enlisting to work,
/// which they are — everything here is scoped per request.
/// </remarks>
public class TransactionScopeFactory(ISqlExecutor sqlExecutor) : ITransactionScopeFactory
{
    private readonly ISqlExecutor _sqlExecutor = sqlExecutor;

    public async Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default)
    {
        ISqlTransactionScope scope = await _sqlExecutor.BeginTransactionAsync(cancellationToken);

        return new Adapter(scope);
    }

    private sealed class Adapter(ISqlTransactionScope inner) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
