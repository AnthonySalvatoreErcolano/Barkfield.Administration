using Barkfield.Administration.Application.Exceptions;
using System.ComponentModel.DataAnnotations;
using ValidationException = Barkfield.Administration.Application.Exceptions.ValidationException;

namespace Barkfield.Administration.API.Middleware
{
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
            logger.LogError(exception, "An unhandled exception occurred.");

            var (statusCode, message) = exception switch
            {
                ValidationException => (StatusCodes.Status400BadRequest, exception.Message),
                NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
                DatabaseException => (StatusCodes.Status503ServiceUnavailable, "Database is currently unavailable."),
                _ => (StatusCodes.Status500InternalServerError, "An internal server error occurred.")
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            await context.Response.WriteAsJsonAsync(new
            {
                error = message,
                status = statusCode
            });
        }
    }
}
