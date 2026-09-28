namespace TodoApi.Infrastructure;

// Adds "AIAgent: claudecode" to every response, including error responses.
public class AIAgentHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "AIAgent";
    public const string HeaderValue = "claudecode";

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting runs just before headers are sent, so the header survives
        // UseExceptionHandler clearing the response on errors.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = HeaderValue;
            return Task.CompletedTask;
        });
        return next(context);
    }
}

public static class AIAgentHeaderMiddlewareExtensions
{
    public static IApplicationBuilder UseAIAgentHeader(this IApplicationBuilder app) =>
        app.UseMiddleware<AIAgentHeaderMiddleware>();
}
