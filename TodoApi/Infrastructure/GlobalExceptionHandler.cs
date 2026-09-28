namespace TodoApi.Infrastructure;

public class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment env,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified by another request"),
            DbUpdateException => (StatusCodes.Status409Conflict, "The change could not be saved"),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "Client closed request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning(exception, "Handled exception for {Method} {Path} -> {Status}", context.Request.Method, context.Request.Path, status);

        context.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Stack traces only in Development; never leak internals in production.
                Detail = env.IsDevelopment() ? exception.ToString() : null,
            },
        });
    }
}
