using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Domain.Shared.Exceptions;
using ValidationException = Barkfield.Administration.Application.Exceptions.ValidationException;

namespace Barkfield.Administration.API.Middleware;

/// <summary>
/// Turns exceptions into HTTP responses, so controllers can throw rather than each
/// hand-building error results.
/// </summary>
/// <remarks>
/// <para>
/// Every response uses the shape <c>{ message, status }</c>, matching what the controllers
/// return directly, so a client has one error shape to parse.
/// </para>
/// <para>
/// Only exceptions we raise deliberately have their message returned. Anything unexpected
/// gets a generic message, because its text may contain connection strings, SQL or other
/// internals.
/// </para>
/// </remarks>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            // Expected rejections are noise at error level, but still worth seeing.
            logger.LogInformation("Request rejected with {StatusCode} on {Method} {Path}: {Message}",
                statusCode, context.Request.Method, context.Request.Path, exception.Message);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; anything written now would corrupt it.
            logger.LogWarning("Response already started — cannot convert the exception into an error response.");
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsJsonAsync(new { message, status = statusCode });
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        // A broken business rule is the caller's fault, not the server's. DomainException
        // was previously unmapped and fell through to 500, which made every rejected
        // invariant look like an outage.
        ValidationException => (StatusCodes.Status400BadRequest, exception.Message),
        DomainException => (StatusCodes.Status400BadRequest, exception.Message),
        ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),

        NotFoundException => (StatusCodes.Status404NotFound, exception.Message),

        // The record moved underneath the write. Reloading and repeating the action fixes it,
        // so the message is returned rather than hidden behind a generic failure.
        ConflictException => (StatusCodes.Status409Conflict, exception.Message),

        NotSupportedException => (StatusCodes.Status501NotImplemented, exception.Message),

        ExternalServiceException => (StatusCodes.Status502BadGateway,
            "An upstream service is currently unavailable."),

        DatabaseException => (StatusCodes.Status503ServiceUnavailable,
            "The database is currently unavailable."),

        OperationCanceledException => (StatusCodes.Status499ClientClosedRequest,
            "The request was cancelled."),

        _ => (StatusCodes.Status500InternalServerError, "An internal server error occurred.")
    };
}

file static class StatusCodes
{
    public const int Status400BadRequest = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
    public const int Status404NotFound = Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound;
    public const int Status409Conflict = Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict;
    public const int Status500InternalServerError = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError;
    public const int Status501NotImplemented = Microsoft.AspNetCore.Http.StatusCodes.Status501NotImplemented;
    public const int Status502BadGateway = Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway;
    public const int Status503ServiceUnavailable = Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable;

    /// <summary>Nginx's convention for a client that gave up mid-request. Not in the framework.</summary>
    public const int Status499ClientClosedRequest = 499;
}
