namespace Barkfield.Administration.Application.Common;

/// <summary>
/// A unit of work spanning several data-access calls.
/// </summary>
/// <remarks>
/// <b>Dispose without committing and the work is rolled back.</b> That is the default on purpose:
/// an exception on any path leaves the database as it was, without every call site having to
/// remember a rollback.
/// </remarks>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens a transaction an application service can hold across several commands.
/// </summary>
/// <remarks>
/// <para>
/// The Application layer cannot see <c>ISqlExecutor</c> — that lives in Infrastructure, and the
/// reference only goes the other way. So the transaction it needs to orchestrate two aggregates
/// is declared here and implemented there, like every other data-access contract.
/// </para>
/// <para>
/// Used where a partial write would be worse than no write: completing a delivery advances the
/// subscription behind it, and cancelling one rolls that subscription's next date forward.
/// </para>
/// <para>
/// Scopes do not nest, and a scope is not for concurrent work — it is one connection, and a
/// connection runs one command at a time.
/// </para>
/// </remarks>
public interface ITransactionScopeFactory
{
    Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default);
}
