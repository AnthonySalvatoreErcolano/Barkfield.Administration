namespace Barkfield.Administration.Application.Exceptions;

/// <summary>
/// A write was rejected because the record changed underneath it.
/// </summary>
/// <remarks>
/// Raised by the optimistic check on an aggregate save. The caller's work is not lost — the
/// right response is to reload and repeat the action, which is why this is a distinct
/// exception and not a generic failure: a 409 tells the client exactly that.
/// </remarks>
public class ConflictException(string message) : Exception(message)
{
}
