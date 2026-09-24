namespace Barkfield.Administration.Infrastructure.Connections.Database;

/// <summary>
/// A database transaction spanning several <see cref="ISqlExecutor"/> calls.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed, the only transaction available was <c>BEGIN TRANSACTION</c> inside a
/// single T-SQL batch, which covers one call and no more. That is enough for an aggregate that
/// is written by one statement, and not enough the moment two aggregates have to move together
/// — completing a delivery advances the subscription's rotation and consumes its add-ons, and
/// billing marks a delivery paid while recording what Square charged. Half of either is worse
/// than neither.
/// </para>
/// <para>
/// <b>Dispose without committing and the work is rolled back.</b> That is the default on
/// purpose: an exception on any path leaves the database as it was, without every call site
/// having to remember a rollback.
/// </para>
/// <para>
/// Usage:
/// <code>
/// await using ISqlTransactionScope tx = await _sql.BeginTransactionAsync(cancellationToken);
///
/// await _deliveryCommands.SaveAsync(delivery, cancellationToken);
/// await _subscriptionCommands.SaveAsync(subscription, revision, cancellationToken);
///
/// await tx.CommitAsync(cancellationToken);
/// </code>
/// Every call between those lines enlists automatically — the commands and queries in a request
/// share one scoped executor, so nothing has to be threaded through their signatures.
/// </para>
/// <para>
/// <b>Not for concurrent work.</b> One scope is one connection, and a connection runs one
/// command at a time. Do not fan out <c>Task.WhenAll</c> inside a scope.
/// </para>
/// </remarks>
public interface ISqlTransactionScope : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction. Calling it twice, or after disposal, throws.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>True until the scope has been committed or disposed.</summary>
    bool IsActive { get; }
}
